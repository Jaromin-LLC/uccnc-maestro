@echo off
REM Jaromin CNC Maestro - installer launcher
REM Double-click this file to open the setup window.
REM For an unattended install from a terminal:
REM   Install.bat -UccncRoot "D:\UCCNC" -Yes [-OverwriteConfigs]

if "%~1"=="" (
    start "" /min powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Install.ps1"
    exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Install.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
    echo Installation FAILED. Review the messages above.
) else (
    echo Installation completed.
)
exit /b %RC%
