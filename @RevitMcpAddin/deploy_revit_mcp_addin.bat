@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%RevitMcpAddin.csproj"
set "BUILD_DLL=%SCRIPT_DIR%bin\Release\RevitMcpAddin.dll"
set "ADDIN_ROOT=%APPDATA%\Autodesk\Revit\Addins\2024"
set "ADDIN_DIR=%ADDIN_ROOT%\RevitMcpAddin"
set "ADDIN_MANIFEST=%ADDIN_ROOT%\RevitMcpAddin.addin"

set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
if not exist "%MSBUILD%" set "MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"

if not exist "%MSBUILD%" (
  echo MSBuild.exe was not found.
  echo Install Visual Studio Build Tools or .NET Framework build tools.
  exit /b 1
)

if not exist "%PROJECT%" (
  echo Project file was not found:
  echo %PROJECT%
  exit /b 1
)

echo Building Revit MCP Addin...
"%MSBUILD%" "%PROJECT%" /p:Configuration=Release /p:Platform=AnyCPU /v:minimal
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

if not exist "%BUILD_DLL%" (
  echo Built DLL was not found:
  echo %BUILD_DLL%
  exit /b 1
)

if not exist "%ADDIN_DIR%" mkdir "%ADDIN_DIR%"

for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "STAMP=%%I"
set "DEST_DLL=%ADDIN_DIR%\RevitMcpAddin_%STAMP%.dll"

echo Copying DLL...
copy /y "%BUILD_DLL%" "%DEST_DLL%" >nul
if errorlevel 1 (
  echo Failed to copy DLL.
  echo Close Revit and try again.
  exit /b 1
)

echo Writing manifest...
(
  echo ^<?xml version="1.0" encoding="utf-8"?^>
  echo ^<RevitAddIns^>
  echo   ^<AddIn Type="Application"^>
  echo     ^<Name^>Revit MCP Addin^</Name^>
  echo     ^<Assembly^>%DEST_DLL%^</Assembly^>
  echo     ^<AddInId^>9E6350C5-0A50-4C25-A6F8-CA8B917BE319^</AddInId^>
  echo     ^<FullClassName^>RevitMcpAddin.App^</FullClassName^>
  echo     ^<VendorId^>CODEX^</VendorId^>
  echo     ^<VendorDescription^>Claude MCP bridge for Autodesk Revit^</VendorDescription^>
  echo   ^</AddIn^>
  echo ^</RevitAddIns^>
) > "%ADDIN_MANIFEST%"
if errorlevel 1 (
  echo Failed to write manifest.
  exit /b 1
)

echo.
echo Deployment complete.
echo Manifest: %ADDIN_MANIFEST%
echo DLL: %DEST_DLL%
echo.
echo Restart Revit to load the deployed add-in.

endlocal
