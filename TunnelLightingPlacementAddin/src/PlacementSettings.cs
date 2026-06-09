namespace TunnelLightingPlacementAddin
{
    public enum PlacementSide
    {
        Center,
        Left,
        Right
    }

    public class PlacementSettings
    {
        public Autodesk.Revit.DB.ElementId FamilySymbolId { get; set; }
        public double StartDistanceMm { get; set; }
        public double EndDistanceMm { get; set; }
        public double SpacingMm { get; set; }
        public PlacementSide Side { get; set; }
        public double OffsetMm { get; set; }
        public double HeightMm { get; set; }
        public string SegmentName { get; set; }
        public string StationParameterName { get; set; }
        public string SegmentParameterName { get; set; }
        public string DirectionParameterName { get; set; }
        public string OffsetParameterName { get; set; }
        public string HeightParameterName { get; set; }
    }
}
