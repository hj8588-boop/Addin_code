using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace TunnelCableTrayPlacementAddin
{
    public class TunnelCenterlineSelectionFilter : ISelectionFilter
    {
        public TunnelCenterlineSelectionFilter(Document document)
        {
        }

        public bool AllowElement(Element elem)
        {
            return elem != null;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return reference != null;
        }
    }
}
