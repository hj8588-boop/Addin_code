using Autodesk.Revit.DB;

namespace TunnelLightingPlacementAddin
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
            string familyName = Symbol.Family == null ? "(Family 없음)" : Symbol.Family.Name;
            return familyName + " : " + Symbol.Name;
        }
    }
}
