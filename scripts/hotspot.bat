@echo off
rem ---------------------------------------------------------------
rem  PhoneDrive - Wi-Fi hotspot (fallback for PCs without PowerShell)
rem  Uses the classic `netsh wlan hostednetwork` mode.
rem  Variables: PD_ACTION, PD_SSID, PD_PASS (8..63), PD_BAND (ignored here)
rem  Output for the app: either OK=1 or OK=0 + ERROR=...
rem ---------------------------------------------------------------
setlocal ENABLEDELAYEDEXPANSION

set "ACT=%PD_ACTION%"
set "SSID=%PD_SSID%"
set "KEY=%PD_PASS%"

if "%ACT%"=="" set "ACT=on"

if /I "%ACT%"=="off" goto do_off
if /I "%ACT%"=="status" goto do_status

if "%SSID%"=="" goto fail_ssid
if "%KEY%"=="" goto fail_key

rem  make sure the previous session is stopped silently
netsh wlan stop hostednetwork >nul 2>nul

netsh wlan set hostednetwork mode=allow ssid="%SSID%" key="%KEY%" >nul 2>nul
if errorlevel 1 (
    netsh wlan set hostednetwork mode=allow ssid="%SSID%" key="%KEY%"
    if errorlevel 1 (
        echo OK=0
        echo ERROR=Cannot configure hosted network. Wi-Fi adapter may not support it.
        exit /b 1
    )
)

netsh wlan start hostednetwork >nul 2>nul
if errorlevel 1 (
    netsh wlan start hostednetwork
    if errorlevel 1 (
        echo OK=0
        echo ERROR=Cannot start hosted network. Wi-Fi adapter may not support it.
        exit /b 1
    )
)

echo OK=1
echo STATE=On
echo SSID=%SSID%
echo HOSTIP=192.168.137.1
echo BAND=
exit /b 0

:do_off
netsh wlan stop hostednetwork >nul 2>nul
echo OK=1
echo STATE=Off
exit /b 0

:do_status
rem  hostednetwork status is language dependent - report Unknown
echo OK=1
echo STATE=Unknown
exit /b 0

:fail_ssid
echo OK=0
echo ERROR=SSID is not set.
exit /b 1

:fail_key
echo OK=0
echo ERROR=Password is not set.
exit /b 1
