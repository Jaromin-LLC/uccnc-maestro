@echo off
REM Jaromin CNC Maestro - installer launcher
REM Double-click this file to install the plugin. The installer auto-detects
REM your UCCNC folder and asks you to confirm it before copying anything.
REM To target a different UCCNC location up front, run from a terminal, e.g.:
REM   Install.bat -UccncRoot "D:\UCCNC"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
    echo Installation FAILED. Review the messages above.
) else (
    echo Installation completed.
)
pause
exit /b %RC%
