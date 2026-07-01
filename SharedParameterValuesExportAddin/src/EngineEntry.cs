using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SharedParameterValuesExportAddin
{
    public static class EngineEntry
    {
        public static Result RunExport(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;

            if (document == null)
            {
                TaskDialog.Show("Shared Parameter Export", "Open a Revit document before running the exporter.");
                return Result.Cancelled;
            }

            using (var form = new ExportSettingsForm(document))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                ExportResult result = ParameterExportService.Export(document, form.Options);
                TaskDialog.Show(
                    "Shared Parameter Export",
                    string.Format(
                        "Export complete.\n\nFile: {0}\nCategories: {1}\nElements: {2}\nParameters: {3}",
                        result.FilePath,
                        result.CategoryCount,
                        result.ElementCount,
                        result.ParameterCount));
                return Result.Succeeded;
            }
        }

        public static Result RunImport(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;

            if (document == null)
            {
                TaskDialog.Show("Shared Parameter Import", "Open a Revit document before running the importer.");
                return Result.Cancelled;
            }

            using (var form = new ImportSettingsForm(document))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                ImportResult result = ParameterImportService.Import(document, form.Options);
                string failedPreview = string.Join("\n", result.Failed.Take(8).ToArray());
                string skippedPreview = string.Join("\n", result.Skipped.Take(8).ToArray());

                TaskDialog.Show(
                    "Shared Parameter Import",
                    string.Format(
                        "Import complete.\n\nFile: {0}\nUpdated elements: {1}\nUpdated values: {2}\nSkipped rows: {3}\nFailed values: {4}\n\nFailed preview:\n{5}\n\nSkipped preview:\n{6}",
                        result.FilePath,
                        result.UpdatedElementCount,
                        result.UpdatedValueCount,
                        result.Skipped.Count,
                        result.Failed.Count,
                        failedPreview,
                        skippedPreview));

                return Result.Succeeded;
            }
        }
    }
}
