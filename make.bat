@echo off
REM Convenience wrapper: make build | make install | make package | make clean
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make.ps1" %*
exit /b %ERRORLEVEL%
