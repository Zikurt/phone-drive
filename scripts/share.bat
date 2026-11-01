@echo off
rem ---------------------------------------------------------------
rem  PhoneDrive - SMB share (fallback for PCs without PowerShell)
rem  Wraps standard net.exe / icacls / netsh commands.
rem  Variables: PD_ACTION, PD_FOLDER, PD_SHARE, PD_USER, PD_PASSWORD,
rem             PD_SUBNET, PD_WEBPORT
rem  Output for the app: either OK=1 or OK=0 + ERROR=...
rem ---------------------------------------------------------------
setlocal ENABLEDELAYEDEXPANSION

set "ACTION=%PD_ACTION%"
set "FOLDER=%PD_FOLDER%"
set "SHARE=%PD_SHARE%"
set "USR=%PD_USER%"
set "PASSWORD=%PD_PASSWORD%"
set "SUBNET=%PD_SUBNET%"
set "WEBPORT=%PD_WEBPORT%"

if "%ACTION%"=="" set "ACTION=on"

if /I "%ACTION%"=="cleanup" goto do_cleanup
if /I "%ACTION%"=="off" goto do_off

rem ---------------- on -------------------------------------------
if "%FOLDER%"=="" goto fail_no_folder
if "%USR%"=="" goto fail_no_user
if not exist "%FOLDER%" md "%FOLDER%"

rem  create the user or update the password of an existing one
net user "%USR%" "%PASSWORD%" /ACTIVE:YES /COMMENT:"PhoneDrive service user" /PASSWORDCHG:NO >nul 2>nul
if errorlevel 1 (
    net user "%USR%" "%PASSWORD%" /ACTIVE:YES /ADD /COMMENT:"PhoneDrive service user" /PASSWORDCHG:NO /EXPIRES:NEVER >nul 2>nul
    if errorlevel 1 (
        echo OK=0
        echo ERROR=Cannot create user '%USR%'. Windows policy may need a stronger password.
        exit /b 1
    )
    net user "%USR%" "%PASSWORD%" >nul 2>nul
)

rem  NTFS permissions
icacls "%FOLDER%" /inheritance:e /grant "%USR%:(OI)(CI)M" /T /C >nul 2>nul
if errorlevel 1 (
    echo OK=0
    echo ERROR=icacls failed to grant permissions on '%FOLDER%'.
    exit /b 1
)

rem  network share
net share "%SHARE%"="%FOLDER%" /grant:"%USR%",CHANGE /UNLIMITED >nul 2>nul
if errorlevel 1 (
    net share "%SHARE%">nul 2>nul
    if errorlevel 1 (
        echo OK=0
        echo ERROR=Cannot create share '%SHARE%'.
        exit /b 1
    )
    net share "%SHARE%"="%FOLDER%" /grant:"%USR%",CHANGE /UNLIMITED >nul 2>nul
)

rem  firewall rules restricted to the subnet
if defined SUBNET (
    netsh advfirewall firewall delete-rule name="PhoneDrive - SMB" >nul 2>nul
    netsh advfirewall firewall add-rule name="PhoneDrive - SMB" group="PhoneDrive" dir=in action=allow protocol=TCP localport=445 remoteip="%SUBNET%" profile=any >nul 2>nul
    if defined WEBPORT (
        netsh advfirewall firewall delete-rule name="PhoneDrive - Web (%WEBPORT%)" >nul 2>nul
        netsh advfirewall firewall add-rule name="PhoneDrive - Web (%WEBPORT%)" group="PhoneDrive" dir=in action=allow protocol=TCP localport=%WEBPORT% remoteip="%SUBNET%" profile=any >nul 2>nul
    )
)

echo OK=1
exit /b 0

rem ---------------- off ------------------------------------------
:do_off
net share "%SHARE%" /DELETE /YES >nul 2>nul
icacls "%FOLDER%" /remove:g "%USR%" /T /C >nul 2>nul
netsh advfirewall firewall delete-rule name="PhoneDrive - SMB" >nul 2>nul
if defined WEBPORT netsh advfirewall firewall delete-rule name="PhoneDrive - Web (%WEBPORT%)" >nul 2>nul
net user "%USR%" /ACTIVE:NO >nul 2>nul
echo OK=1
exit /b 0

rem ---------------- cleanup --------------------------------------
:do_cleanup
net share "%SHARE%" /DELETE /YES >nul 2>nul
icacls "%FOLDER%" /remove:g "%USR%" /T /C >nul 2>nul
netsh advfirewall firewall delete-rule name="PhoneDrive - SMB" >nul 2>nul
if defined WEBPORT netsh advfirewall firewall delete-rule name="PhoneDrive - Web (%WEBPORT%)" >nul 2>nul
net user "%USR%" /DELETE >nul 2>nul
echo OK=1
exit /b 0

rem ---------------- errors ---------------------------------------
:fail_no_folder
echo OK=0
echo ERROR=Target folder is not set.
exit /b 1

:fail_no_user
echo OK=0
echo ERROR=Login is not set.
exit /b 1
