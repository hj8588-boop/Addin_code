@echo off
setlocal
cd /d "%~dp0"
echo Revit is using add-in DLLs while it is running.
echo Close Revit before installing RevitMcpAddin.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_revit2024.ps1"
echo.
pause
