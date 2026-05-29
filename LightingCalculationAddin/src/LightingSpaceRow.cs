using Autodesk.Revit.DB;

namespace LightingCalculationAddin
{
    public class LightingSpaceRow
    {
        public bool IsSelected { get; set; }
        public ElementId SpaceId { get; set; }
        public int ElementIdValue
        {
            get { return SpaceId == null ? 0 : SpaceId.IntegerValue; }
        }
        public string SpaceName { get; set; }
        public string LevelName { get; set; }
        public double AreaM2 { get; set; }
        public double LengthM { get; set; }
        public double WidthM { get; set; }
        public double RequiredLux { get; set; }
        public double EffectiveHeightM { get; set; }
        public double CeilingReflectance { get; set; }
        public double WallReflectance { get; set; }
        public double FloorReflectance { get; set; }
        public double UtilizationFactor { get; set; }
        public double MaintenanceFactor { get; set; }
        public string FixtureType { get; set; }
        public double FixtureFluxLm { get; set; }
        public double RoomIndex { get; set; }
        public double RawRequiredCount { get; set; }
        public int RequiredCount { get; set; }
        public double CalculatedIlluminance { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }
}
