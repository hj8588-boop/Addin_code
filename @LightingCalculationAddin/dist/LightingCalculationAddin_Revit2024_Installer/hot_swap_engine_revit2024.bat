@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package_hot_swap_engine_revit2024.ps1"
echo.
pause
