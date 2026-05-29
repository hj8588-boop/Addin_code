# Shared Parameter Values Export Add-in

This is a Revit 2024 C# add-in version of `#SharedParameterValues_Export/SharedParameterValues_Export.dyn`.

## What It Does

- Selects Revit categories.
- Collects instance elements in those categories.
- Exports `ElementId`, `Category`, `Family`, `Type`, and shared parameter values.
- If parameter names are blank, it auto-detects shared parameter names from the selected elements.
- Writes `.xlsx` directly, so Microsoft Excel is not required.
- Imports edited `.csv`, `.xlsx`, or `.xlsm` values back into existing Revit elements by `UniqueId` or `ElementId`.
- Import headers ending in `[Instance]` update instance parameters; headers ending in `[Type]` update type parameters.

## Build

Open `SharedParameterValuesExportAddin.csproj` in Visual Studio and build `Release`.

This PC also has the .NET Framework compiler available, so you can build without Visual Studio:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\build_revit2024.ps1
```

The project references Revit 2024 here:

```xml
<RevitInstallDir>C:\Program Files\Autodesk\Revit 2024\</RevitInstallDir>
```

For another Revit version, update that path and the target framework/API compatibility as needed.

## Install For Revit 2024

After building, run PowerShell from this folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\install_revit2024.ps1
```

Restart Revit. The command appears at:

`Codex Tools` tab -> `Parameters` panel -> `Shared Parameter Export`

`Codex Tools` tab -> `Parameters` panel -> `Shared Parameter Import`
