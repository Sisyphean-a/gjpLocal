@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\uninstall-windows-service.ps1"
if errorlevel 1 (
  echo.
  echo [SWCS] Service uninstall failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo [SWCS] Service uninstalled.
endlocal
