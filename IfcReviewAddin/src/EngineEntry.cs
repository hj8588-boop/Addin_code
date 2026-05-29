using Autodesk.Revit.DB;

namespace IfcReviewAddin
{
    public static class EngineEntry
    {
        public static void Show(Document document)
        {
            using (var form = new IfcReviewForm(document))
            {
                form.ShowDialog();
            }
        }
    }
}
