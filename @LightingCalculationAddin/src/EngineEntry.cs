using Autodesk.Revit.DB;

namespace LightingCalculationAddin
{
    public static class EngineEntry
    {
        public static void Show(Document document)
        {
            using (var form = new LightingCalculationForm(document))
            {
                form.ShowDialog();
            }
        }
    }
}
