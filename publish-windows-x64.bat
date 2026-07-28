@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-windows-x64.ps1" %*
exit /b %ERRORLEVEL%
