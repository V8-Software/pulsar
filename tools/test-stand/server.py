#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Тестовый стенд V8:Pulsar — минимальный MCP-клиент + HTTP-сервер.
Запуск: python server.py
Открыть: http://localhost:7100
"""

import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import threading
import time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path

PORT = 7100

# ────────────────────────────────────────────────────────────────
# MCP-клиент (stdio JSON-RPC)
# ────────────────────────────────────────────────────────────────

class MCPClient:
    def __init__(self, exe_path: Path):
        self.exe_path = exe_path
        self.proc = None
        self._lock = threading.Lock()
        self._responses = {}
        self._reader = None
        self._next_id = 1
        self._ready = threading.Event()

    def start(self):
        print(f"[MCP] Запуск {self.exe_path} --mcp")
        self.proc = subprocess.Popen(
            [str(self.exe_path), "--mcp"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
            cwd=str(self.exe_path.parent),
        )

        self._reader = threading.Thread(target=self._reader_loop, daemon=True)
        self._reader.start()

        # 1) initialize
        init_id = 0
        self._send({
            "jsonrpc": "2.0",
            "id": init_id,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "pulsar-testbed", "version": "1.0.0"},
            },
        })

        resp = self._wait(init_id, timeout=15)
        if not resp:
            raise RuntimeError("MCP-сервер не ответил на initialize")

        print(f"[MCP] initialize OK: {resp.get('result', {}).get('serverInfo', {})}")

        # 2) notifications/initialized
        self._send({
            "jsonrpc": "2.0",
            "method": "notifications/initialized",
        })

        self._ready.set()
        print(f"[MCP] Готов к работе")

    def _reader_loop(self):
        """Читает line-delimited JSON из stdout MCP."""
        while True:
            try:
                line = self.proc.stdout.readline()
            except Exception:
                break
            if not line:
                break
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                # Логи/мусор попадают в stderr, stdout должен быть чистым JSON
                print(f"[MCP] не-JSON: {line[:200]}", file=sys.stderr)
                continue

            msg_id = msg.get("id")
            if msg_id is not None:
                with self._lock:
                    self._responses[msg_id] = msg

    def _send(self, obj: dict):
        payload = json.dumps(obj, ensure_ascii=False) + "\n"
        self.proc.stdin.write(payload)
        self.proc.stdin.flush()

    def _wait(self, msg_id, timeout=30) -> dict | None:
        deadline = time.time() + timeout
        while time.time() < deadline:
            with self._lock:
                if msg_id in self._responses:
                    return self._responses.pop(msg_id)
            time.sleep(0.05)
        return None

    def call_tool(self, name: str, arguments: dict | None = None) -> dict:
        if self.proc is None or self.proc.poll() is not None:
            print("[MCP] Процесс завершён, запускаю заново")
            self.start()

        self._ready.wait(timeout=10)
        with self._lock:
            req_id = self._next_id
            self._next_id += 1

        request = {
            "jsonrpc": "2.0",
            "id": req_id,
            "method": "tools/call",
            "params": {
                "name": name,
                "arguments": arguments or {},
            },
        }
        try:
            self._send(request)
        except OSError:
            print("[MCP] Канал недоступен, запускаю процесс заново")
            self.stop()
            self.start()
            self._send(request)

        resp = self._wait(req_id, timeout=30)
        if resp is None:
            raise TimeoutError(f"Таймаут вызова инструмента {name}")
        return resp

    def stop(self):
        if self.proc and self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.proc.kill()


# ────────────────────────────────────────────────────────────────
# HTTP-сервер
# ────────────────────────────────────────────────────────────────

mcp: MCPClient | None = None

_screenshot_paths: dict[str, Path] = {}
_screenshot_paths_lock = threading.Lock()
_screenshot_line_re = re.compile(
    r'(?im)^(?:path|afterScreenshot|diffScreenshot|diffRegions\[\d+\])\s*:\s*"?(.+?\.png)"?\s*$'
)


def register_returned_screenshots(result: object) -> list[dict[str, str]]:
    """Регистрирует только существующие PNG, явно перечисленные в ответе Пульсара."""
    candidates: list[str] = []

    def collect(value: object):
        if isinstance(value, dict):
            for child in value.values():
                collect(child)
            return
        if isinstance(value, list):
            for child in value:
                collect(child)
            return
        if not isinstance(value, str):
            return

        for match in _screenshot_line_re.finditer(value):
            candidates.append(match.group(1).strip())

        stripped = value.strip()
        if stripped.startswith(("{", "[")):
            try:
                collect(json.loads(stripped))
            except json.JSONDecodeError:
                pass

    collect(result)

    screenshots: list[dict[str, str]] = []
    seen: set[Path] = set()
    for candidate in candidates:
        try:
            path = Path(candidate).resolve(strict=True)
        except (OSError, RuntimeError):
            continue
        if path.suffix.lower() != ".png" or not path.is_file() or path in seen:
            continue

        seen.add(path)
        token = secrets.token_urlsafe(24)
        with _screenshot_paths_lock:
            _screenshot_paths[token] = path
            while len(_screenshot_paths) > 200:
                _screenshot_paths.pop(next(iter(_screenshot_paths)))
        screenshots.append({
            "path": candidate,
            "url": f"/api/screenshot/{token}",
        })

    return screenshots


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        # тихий лог
        pass

    def _cors(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def _json(self, data, status=200):
        body = json.dumps(data, ensure_ascii=False, indent=2)
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self._cors()
        self.end_headers()
        self.wfile.write(body.encode("utf-8"))

    def _html(self, body: str, status=200):
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.end_headers()
        self.wfile.write(body.encode("utf-8"))

    def do_OPTIONS(self):
        self.send_response(204)
        self._cors()
        self.end_headers()

    def do_GET(self):
        if self.path.startswith("/api/screenshot/"):
            token = self.path.removeprefix("/api/screenshot/")
            with _screenshot_paths_lock:
                path = _screenshot_paths.get(token)
            if path is None or not path.is_file():
                self.send_error(404)
                return

            try:
                size = path.stat().st_size
                self.send_response(200)
                self.send_header("Content-Type", "image/png")
                self.send_header("Content-Length", str(size))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                with path.open("rb") as image_file:
                    while chunk := image_file.read(64 * 1024):
                        self.wfile.write(chunk)
            except OSError:
                self.send_error(404)
            return

        if self.path in ("/", "/index.html"):
            html_path = Path(__file__).with_name("index.html")
            self._html(html_path.read_text(encoding="utf-8"))
        else:
            self.send_error(404)

    def do_POST(self):
        if self.path != "/api/call":
            self.send_error(404)
            return

        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length).decode("utf-8")
        try:
            req = json.loads(body)
        except json.JSONDecodeError as e:
            self._json({"ok": False, "error": f"Invalid JSON: {e}"}, 400)
            return

        tool = req.get("tool")
        args = req.get("args", {})
        if not tool:
            self._json({"ok": False, "error": "Не указан tool"}, 400)
            return

        try:
            result = mcp.call_tool(tool, args)
            screenshots = register_returned_screenshots(result)
            self._json({"ok": True, "result": result, "screenshots": screenshots})
        except Exception as e:
            self._json({"ok": False, "error": str(e)}, 500)


# ────────────────────────────────────────────────────────────────
# Утилиты
# ────────────────────────────────────────────────────────────────

def find_exe() -> Path | None:
    """Ищет v8-pulsar.exe в явной настройке, рабочей сборке и установке."""
    configured = os.environ.get("V8_PULSAR_EXE", "").strip()
    if configured:
        candidate = Path(os.path.expandvars(configured)).expanduser()
        if candidate.is_file():
            return candidate.resolve()

    stand_dir = Path(__file__).resolve().parent
    development_patterns = [
        "v8-pulsar/bin/Release/net9.0-windows*/win-x64/v8-pulsar.exe",
        "v8-pulsar/bin/Release/net9.0-windows*/v8-pulsar.exe",
        "v8-pulsar/bin/Debug/net9.0-windows*/win-x64/v8-pulsar.exe",
        "v8-pulsar/bin/Debug/net9.0-windows*/v8-pulsar.exe",
        "v8-pulsar/bin/Release-check/v8-pulsar.exe",
        "v8-pulsar/bin/Test-check/v8-pulsar.exe",
    ]
    for base in (stand_dir.parent, stand_dir.parent.parent):
        for pattern in development_patterns:
            for match in base.glob(pattern):
                if match.is_file():
                    return match.resolve()

    candidates = [stand_dir / "v8-pulsar.exe"]
    for variable in ("ProgramFiles", "LOCALAPPDATA"):
        root = os.environ.get(variable)
        if root:
            candidates.append(Path(root) / "V8Pulsar" / "v8-pulsar.exe")
            candidates.append(Path(root) / "Programs" / "V8Pulsar" / "v8-pulsar.exe")

    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()

    from_path = shutil.which("v8-pulsar.exe")
    if from_path:
        return Path(from_path).resolve()

    return None


# ────────────────────────────────────────────────────────────────
# main
# ────────────────────────────────────────────────────────────────

def main():
    global mcp

    exe = find_exe()
    if not exe:
        print("ОШИБКА: не найден v8-pulsar.exe")
        print("Укажите путь через переменную окружения V8_PULSAR_EXE")
        sys.exit(1)

    print(f"[INFO] Найден: {exe}")

    mcp = MCPClient(exe)
    try:
        mcp.start()
    except Exception as e:
        print(f"[FATAL] Не удалось запустить MCP: {e}")
        sys.exit(1)

    server = ThreadingHTTPServer(("localhost", PORT), Handler)
    print(f"[INFO] Стенд: http://localhost:{PORT}")
    print("[INFO] Ctrl+C для остановки")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[INFO] Остановка...")
    finally:
        server.server_close()
        mcp.stop()


if __name__ == "__main__":
    main()
