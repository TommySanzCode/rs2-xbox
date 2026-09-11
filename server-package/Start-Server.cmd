@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Configure-Server.ps1"
set "rs2ExitCode=%errorlevel%"
if not "%rs2ExitCode%"=="0" goto finish
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-RS2-Server.ps1" %*
set "rs2ExitCode=%errorlevel%"
:finish
echo.
if not "%rs2ExitCode%"=="0" echo Server startup failed. Read the message above for details.
pause
exit /b %rs2ExitCode%
