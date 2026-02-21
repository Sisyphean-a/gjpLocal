@echo off
setlocal
cd /d "%~dp0"

if /I "%~1"=="nobuild" goto nobuild

echo [SWCS] Start with frontend build...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\init-and-start.ps1" -BuildFrontend -Environment Production
goto end

:nobuild
echo [SWCS] Start without frontend rebuild...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\init-and-start.ps1" -BuildFrontend:$false -Environment Production

:end
if errorlevel 1 (
  echo.
  echo [SWCS] Start failed. Press any key to exit.
  pause >nul
)

endlocal
