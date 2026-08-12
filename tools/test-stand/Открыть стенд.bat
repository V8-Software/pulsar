@echo off
chcp 65001 >nul
set "DIR=%~dp0"
cd /d "%DIR%"

:: Проверяем порт
timeout /t 1 /nobreak >nul
python -c "import socket; s=socket.socket(); s.bind(('127.0.0.1',7100)); s.close()" >nul 2>&1
if %errorlevel%==0 (
    echo [INFO] Запуск сервера...
    start "" pythonw server.py
    timeout /t 3 /nobreak >nul
) else (
    echo [INFO] Сервер уже работает
)

echo [INFO] Открываю браузер...
start http://localhost:7100
