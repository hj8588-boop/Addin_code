using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SharedParameterValuesExportAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDocument = commandData.Application.ActiveUIDocument;
                Document document = uiDocument == null ? null : uiDocument.Document;

                if (document == null)
                {
                    message = "Open a Revit document before running the exporter.";
                    TaskDialog.Show("Shared Parameter Export", message);
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
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Shared Parameter Export - Error", ex.ToString());
                return Result.Cancelled;
            }
        }
    }
}
