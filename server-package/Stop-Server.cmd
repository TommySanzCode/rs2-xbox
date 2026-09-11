@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Stop-RS2-Server.ps1" %*
set "rs2ExitCode=%errorlevel%"
echo.
if not "%rs2ExitCode%"=="0" echo Server shutdown was not completed. Read the message above for details.
pause
exit /b %rs2ExitCode%
