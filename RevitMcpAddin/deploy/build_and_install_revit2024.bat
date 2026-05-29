@echo off
setlocal
cd /d "%~dp0"
echo Revit is using add-in DLLs while it is running.
echo Close Revit before building and installing RevitMcpAddin.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_revit2024.ps1"
if errorlevel 1 goto :failed
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_revit2024.ps1"
if errorlevel 1 goto :failed
echo.
echo RevitMcpAddin build and install completed.
echo.
pause
exit /b 0

:failed
echo.
echo RevitMcpAddin build or install failed.
echo.
pause
exit /b 1
