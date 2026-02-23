@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Restart-Service -Name 'SwcsScanner' -ErrorAction Stop } catch { Write-Error $_; exit 1 }"
if errorlevel 1 (
  echo.
  echo [SWCS] Service restart failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo [SWCS] Service restarted.
endlocal
