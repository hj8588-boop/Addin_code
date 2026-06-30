using Autodesk.Revit.DB;

namespace CableGeneratorAddin
{
    public class CableGeneratorSettings
    {
        public ElementId ConduitTypeId { get; set; }
        public int CableCount { get; set; }
        public double CableDiameterMm { get; set; }
        public double GapMm { get; set; }
        public double TrayOffsetMm { get; set; }
    }
}
