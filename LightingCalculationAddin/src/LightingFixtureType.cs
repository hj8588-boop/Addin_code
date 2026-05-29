using Autodesk.Revit.DB;

namespace LightingCalculationAddin
{
    public class LightingFixtureType
    {
        public ElementId TypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string Label { get; set; }
        public double FluxLm { get; set; }
        public string FluxParameterName { get; set; }
    }
}
