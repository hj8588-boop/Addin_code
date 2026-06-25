using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace TroughAutoPlacementAddin
{
    public class EdgeSelectionFilter : ISelectionFilter
    {
        private readonly Document _document;

        public EdgeSelectionFilter(Document document)
        {
            _document = document;
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null)
                return false;

            if (elem is ImportInstance || elem is RevitLinkInstance || elem is CurveElement)
                return true;

            if (elem.Location is LocationCurve)
                return true;

            Category category = elem.Category;
            if (category == null)
                return true;

            return category.Id.Value == (long)BuiltInCategory.OST_GenericModel
                || category.Id.Value == (long)BuiltInCategory.OST_IOSModelGroups
                || elem is FamilyInstance
                || elem is Group;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            if (reference == null)
                return false;

            Element element = _document.GetElement(reference.ElementId);
            if (element == null)
                return false;

            GeometryObject geometryObject = GetReferencedGeometryObject(element, reference);
            return geometryObject is Edge
                || geometryObject is Curve
                || geometryObject is PolyLine;
        }

        private static GeometryObject GetReferencedGeometryObject(Element element, Reference reference)
        {
            RevitLinkInstance linkInstance = element as RevitLinkInstance;
            if (linkInstance != null && reference.LinkedElementId != ElementId.InvalidElementId)
                return GetLinkedReferencedGeometryObject(linkInstance, reference);

            try
            {
                return element.GetGeometryObjectFromReference(reference);
            }
            catch
            {
                return null;
            }
        }

        private static GeometryObject GetLinkedReferencedGeometryObject(RevitLinkInstance linkInstance, Reference reference)
        {
            Document linkedDocument = linkInstance.GetLinkDocument();
            Element linkedElement = linkedDocument == null ? null : linkedDocument.GetElement(reference.LinkedElementId);
            if (linkedElement == null)
                return null;

            try
            {
                Reference linkedReference = reference.CreateReferenceInLink();
                return linkedReference == null ? null : linkedElement.GetGeometryObjectFromReference(linkedReference);
            }
            catch
            {
                return null;
            }
        }
    }
}
