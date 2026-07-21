using Autodesk.Revit.UI;

namespace RevitSheetBatchEditor.Engine
{
    public static class EntryPoint
    {
        public static Result Run(ExternalCommandData commandData)
        {
            var window = new SheetBatchWindow(commandData.Application.ActiveUIDocument.Document);
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
