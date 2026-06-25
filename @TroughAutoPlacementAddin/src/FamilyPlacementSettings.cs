using Autodesk.Revit.DB;

namespace TroughAutoPlacementAddin
{
    public class FamilyPlacementSettings
    {
        public ElementId FamilySymbolId { get; set; }
        public double SpacingMm { get; set; }
        public double ToleranceMm { get; set; }
        public int RotationMode { get; set; }
        public double PerpendicularOffsetMm { get; set; }
        public double ParallelOffsetMm { get; set; }
    }
}
