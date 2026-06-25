using Autodesk.Revit.DB;

namespace TroughAutoPlacementAddin
{
    public class FamilySymbolListItem
    {
        public FamilySymbolListItem(FamilySymbol symbol)
        {
            Symbol = symbol;
        }

        public FamilySymbol Symbol { get; private set; }

        public override string ToString()
        {
            if (Symbol == null)
                return "(No family type)";

            string categoryName = Symbol.Category == null ? "No category" : Symbol.Category.Name;
            string familyName = Symbol.Family == null ? "No family" : Symbol.Family.Name;
            return categoryName + " / " + familyName + " : " + Symbol.Name;
        }
    }
}
