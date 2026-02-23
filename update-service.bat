@echo off
setlocal
cd /d "%~dp0"

set "BUILD_FRONTEND_ARG=-BuildFrontend"
set "ELEVATED_FLAG=0"
set "SHOW_HELP=0"

for %%I in (%*) do (
  if /I "%%~I"=="nobuild" set "BUILD_FRONTEND_ARG=-BuildFrontend:$false"
  if /I "%%~I"=="--elevated" set "ELEVATED_FLAG=1"
  if /I "%%~I"=="/?" set "SHOW_HELP=1"
  if /I "%%~I"=="-h" set "SHOW_HELP=1"
  if /I "%%~I"=="--help" set "SHOW_HELP=1"
)

if "%SHOW_HELP%"=="1" (
  echo Usage:
  echo   update-service.bat
  echo   update-service.bat nobuild
  echo.
  echo Options:
  echo   nobuild   Skip frontend build and only publish/restart backend service.
  exit /b 0
)

if "%ELEVATED_FLAG%"=="0" (
  net session >nul 2>&1
  if errorlevel 1 (
    echo [SWCS] Requesting administrator permissions...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%* --elevated' -Verb RunAs"
    if errorlevel 1 (
      echo.
      echo [SWCS] Elevation failed. Press any key to exit.
      pause >nul
      exit /b 1
    )
    exit /b 0
  )
)

echo [SWCS] Updating service (build + publish + restart)...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\ops\ps\install-windows-service.ps1" %BUILD_FRONTEND_ARG% -Environment Production
if errorlevel 1 (
  echo.
  echo [SWCS] Service update failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo [SWCS] Service updated and restarted.
endlocal
