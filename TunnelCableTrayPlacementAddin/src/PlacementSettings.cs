using Autodesk.Revit.DB;

namespace TunnelCableTrayPlacementAddin
{
    public class PlacementSettings
    {
        public ElementId CableTrayTypeId { get; set; }
        public double StartDistanceMm { get; set; }
        public double EndDistanceMm { get; set; }
        public double SegmentLengthMm { get; set; }
        public double OffsetMm { get; set; }
        public double ElevationMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
    }
}
