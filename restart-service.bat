@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\restart-windows-service.ps1"
if errorlevel 1 (
  echo.
  echo [SWCS] Service restart failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo [SWCS] Service restarted.
endlocal
