import json
import tempfile
import threading
import unittest
import urllib.request
from pathlib import Path
from unittest import mock

import server
from server import Handler, MCPClient


class _FakeStdin:
    def __init__(self, error=None):
        self.error = error
        self.writes = []

    def write(self, value):
        if self.error is not None:
            raise self.error
        self.writes.append(value)

    def flush(self):
        pass


class _FakeProcess:
    def __init__(self, exit_code, write_error=None):
        self.exit_code = exit_code
        self.stdin = _FakeStdin(write_error)

    def poll(self):
        return self.exit_code

    def terminate(self):
        self.exit_code = 0

    def wait(self, timeout=None):
        return self.exit_code

    def kill(self):
        self.exit_code = -9


class _RecoveringClient(MCPClient):
    def __init__(self):
        super().__init__(Path("v8-pulsar.exe"))
        self.proc = _FakeProcess(exit_code=1)
        self._ready.set()
        self.start_count = 0

    def start(self):
        self.start_count += 1
        self.proc = _FakeProcess(exit_code=None)
        self._ready.set()

    def _wait(self, msg_id, timeout=30):
        return {"id": msg_id, "result": {"content": []}}


class _WriteRecoveryClient(_RecoveringClient):
    def __init__(self):
        super().__init__()
        self.proc = _FakeProcess(
            exit_code=None,
            write_error=OSError(22, "Invalid argument"),
        )


class MCPClientRecoveryTests(unittest.TestCase):
    def test_call_restarts_mcp_process_terminated_by_build(self):
        client = _RecoveringClient()

        response = client.call_tool("list_sessions")

        self.assertEqual(client.start_count, 1)
        self.assertEqual(response["result"]["content"], [])

    def test_call_restarts_when_process_ended_between_check_and_write(self):
        client = _WriteRecoveryClient()

        response = client.call_tool("list_sessions")

        self.assertEqual(client.start_count, 1)
        self.assertEqual(response["result"]["content"], [])


class FindExeTests(unittest.TestCase):
    def test_prefers_explicit_environment_path(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            executable = Path(temp_dir) / "v8-pulsar.exe"
            executable.touch()

            with mock.patch.dict(server.os.environ, {"V8_PULSAR_EXE": str(executable)}, clear=False):
                self.assertEqual(server.find_exe(), executable.resolve())

    def test_finds_local_development_build(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            repository = Path(temp_dir) / "workspace"
            stand_file = repository / "ТестовыйСтенд" / "server.py"
            executable = (
                repository
                / "v8-pulsar"
                / "bin"
                / "Release"
                / "net9.0-windows10.0.17763.0"
                / "win-x64"
                / "v8-pulsar.exe"
            )
            stand_file.parent.mkdir(parents=True)
            executable.parent.mkdir(parents=True)
            stand_file.touch()
            executable.touch()

            with mock.patch.object(server, "__file__", str(stand_file)), mock.patch.dict(
                server.os.environ,
                {"PATH": ""},
                clear=True,
            ):
                self.assertEqual(server.find_exe(), executable.resolve())

    def test_finds_installed_application(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            local_app_data = Path(temp_dir) / "LocalAppData"
            executable = local_app_data / "Programs" / "V8Pulsar" / "v8-pulsar.exe"
            stand_file = Path(temp_dir) / "public" / "tools" / "test-stand" / "server.py"
            executable.parent.mkdir(parents=True)
            stand_file.parent.mkdir(parents=True)
            executable.touch()
            stand_file.touch()

            with mock.patch.object(server, "__file__", str(stand_file)), mock.patch.dict(
                server.os.environ,
                {"LOCALAPPDATA": str(local_app_data), "PATH": ""},
                clear=True,
            ):
                self.assertEqual(server.find_exe(), executable.resolve())

    def test_returns_none_when_no_candidate_exists(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            stand_file = Path(temp_dir) / "public" / "tools" / "test-stand" / "server.py"
            stand_file.parent.mkdir(parents=True)
            stand_file.touch()

            with mock.patch.object(server, "__file__", str(stand_file)), mock.patch.dict(
                server.os.environ,
                {"V8_PULSAR_EXE": "Z:\\missing\\v8-pulsar.exe", "PATH": ""},
                clear=True,
            ):
                self.assertIsNone(server.find_exe())


class _ScreenshotMCP:
    def __init__(self, screenshot_path):
        self.screenshot_path = screenshot_path

    def call_tool(self, name, arguments):
        return {
            "result": {
                "content": [
                    {
                        "type": "text",
                        "text": f"success: true\npath: {self.screenshot_path}",
                    }
                ]
            }
        }


class ScreenshotHttpTests(unittest.TestCase):
    def test_call_registers_returned_png_and_serves_it_by_opaque_url(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            screenshot = Path(temp_dir) / "capture.png"
            expected = b"\x89PNG\r\n\x1a\nstand-test"
            screenshot.write_bytes(expected)

            server.mcp = _ScreenshotMCP(screenshot)
            httpd = server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
            thread = threading.Thread(target=httpd.serve_forever, daemon=True)
            thread.start()
            base_url = f"http://127.0.0.1:{httpd.server_port}"

            try:
                request = urllib.request.Request(
                    base_url + "/api/call",
                    data=json.dumps({"tool": "capture_screenshot", "args": {}}).encode("utf-8"),
                    headers={"Content-Type": "application/json"},
                    method="POST",
                )
                with urllib.request.urlopen(request) as response:
                    payload = json.loads(response.read().decode("utf-8"))

                self.assertEqual(payload["screenshots"][0]["path"], str(screenshot))
                image_url = payload["screenshots"][0]["url"]
                self.assertTrue(image_url.startswith("/api/screenshot/"))
                self.assertNotIn(str(screenshot), image_url)

                with urllib.request.urlopen(base_url + image_url) as response:
                    self.assertEqual(response.headers.get_content_type(), "image/png")
                    self.assertEqual(response.read(), expected)
            finally:
                httpd.shutdown()
                httpd.server_close()
                thread.join(timeout=5)
                server.mcp = None

if __name__ == "__main__":
    unittest.main()
