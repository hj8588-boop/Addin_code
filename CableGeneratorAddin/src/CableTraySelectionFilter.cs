using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace CableGeneratorAddin
{
    public class CableTraySelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is CableTray)
                return true;

            Category category = elem == null ? null : elem.Category;
            return category != null
                && category.Id.Value == (long)BuiltInCategory.OST_CableTrayFitting;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return reference != null;
        }
    }
}
