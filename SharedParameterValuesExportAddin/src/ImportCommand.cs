using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SharedParameterValuesExportAddin
{
    [Transaction(TransactionMode.Manual)]
    public class ImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDocument = commandData.Application.ActiveUIDocument;
                Document document = uiDocument == null ? null : uiDocument.Document;

                if (document == null)
                {
                    message = "Open a Revit document before running the importer.";
                    TaskDialog.Show("Shared Parameter Import", message);
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
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Shared Parameter Import - Error", ex.ToString());
                return Result.Cancelled;
            }
        }
    }
}
