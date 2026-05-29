@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0build_revit2024.ps1"
if errorlevel 1 exit /b %errorlevel%
powershell -ExecutionPolicy Bypass -File "%~dp0install_revit2024.ps1"
