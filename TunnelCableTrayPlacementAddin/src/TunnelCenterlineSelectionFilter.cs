using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace TunnelCableTrayPlacementAddin
{
    public class TunnelCenterlineSelectionFilter : ISelectionFilter
    {
        private readonly Document _document;

        public TunnelCenterlineSelectionFilter(Document document)
        {
            _document = document;
        }

        public bool AllowElement(Element elem)
        {
            if (elem is RevitLinkInstance)
                return true;

            if (TunnelCableTrayPlacementService.GetCurveFromElement(elem) != null)
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
            Element element = _document == null || reference == null
                ? null
                : _document.GetElement(reference.ElementId);
            if (element is RevitLinkInstance)
                return true;

            return TunnelCableTrayPlacementService.GetCurveFromReference(_document, reference) != null;
        }
    }
}
