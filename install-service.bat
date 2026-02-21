@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\install-windows-service.ps1" -BuildFrontend -Environment Production
if errorlevel 1 (
  echo.
  echo [SWCS] Service installation failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo [SWCS] Service installed and started.
endlocal
