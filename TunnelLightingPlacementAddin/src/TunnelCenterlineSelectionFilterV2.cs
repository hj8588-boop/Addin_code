using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace TunnelLightingPlacementAddin
{
    public class TunnelCenterlineSelectionFilterV2 : ISelectionFilter
    {
        private readonly Document _document;

        public TunnelCenterlineSelectionFilterV2(Document document)
        {
            _document = document;
        }

        public bool AllowElement(Element elem)
        {
            if (TunnelLightingPlacementServiceV2.GetCurveFromElement(elem) != null)
                return true;

            Category category = elem == null ? null : elem.Category;
            if (category == null)
                return false;

            return category.Id.Value == (long)BuiltInCategory.OST_GenericModel
                || category.Id.Value == (long)BuiltInCategory.OST_IOSModelGroups
                || elem is FamilyInstance
                || elem is Group;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return TunnelLightingPlacementServiceV2.GetCurveFromReference(_document, reference) != null;
        }
    }
}
