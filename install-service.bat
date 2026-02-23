@echo off
setlocal
cd /d "%~dp0"

if /I not "%~1"=="--elevated" (
  net session >nul 2>&1
  if errorlevel 1 (
    echo [SWCS] Requesting administrator permissions...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--elevated' -Verb RunAs"
    if errorlevel 1 (
      echo.
      echo [SWCS] Elevation failed. Press any key to exit.
      pause >nul
      exit /b 1
    )
    exit /b 0
  )
)

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
