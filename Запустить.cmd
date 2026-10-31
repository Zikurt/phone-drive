@echo off
setlocal
cd /d "%~dp0"

if not exist "bin\PhoneDrive.exe" (
  echo Building PhoneDrive...
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
  if errorlevel 1 (
    echo.
    echo Build failed. See messages above.
    pause
    exit /b 1
  )
)

start "" "%~dp0bin\PhoneDrive.exe"
