namespace TunnelLightingPlacementAddin
{
    public class PlacementSettings
    {
        public Autodesk.Revit.DB.ElementId FamilySymbolId { get; set; }
        public double StartDistanceMm { get; set; }
        public double EndDistanceMm { get; set; }
        public double SpacingMm { get; set; }
        public double OffsetMm { get; set; }
        public double HeightMm { get; set; }
        public double RotationAngleDegrees { get; set; }
    }
}
