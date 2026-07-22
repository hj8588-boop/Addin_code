using Autodesk.Revit.DB.Electrical;

namespace CableGeneratorAddin
{
    public class ConduitTypeListItem
    {
        public ConduitTypeListItem(ConduitType type)
        {
            Type = type;
        }

        public ConduitType Type { get; private set; }

        public override string ToString()
        {
            return Type == null ? "(No conduit type)" : Type.Name;
        }
    }
}
