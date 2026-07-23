using Autodesk.Revit.DB.Electrical;

namespace TunnelCableTrayPlacementAddin
{
    public class CableTrayTypeListItem
    {
        public CableTrayTypeListItem(CableTrayType type)
        {
            Type = type;
        }

        public CableTrayType Type { get; private set; }

        public override string ToString()
        {
            return Type == null ? "(Cable Tray Type 없음)" : Type.Name;
        }
    }
}
