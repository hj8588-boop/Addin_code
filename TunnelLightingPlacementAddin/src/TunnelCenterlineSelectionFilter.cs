using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace TunnelLightingPlacementAddin
{
    public class TunnelCenterlineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return TunnelLightingPlacementService.GetCurveFromElement(elem) != null;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
