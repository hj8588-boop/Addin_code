using System.Collections.Generic;

namespace IfcReviewAddin
{
    public class IfcReviewRow
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string RevitUniqueId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public int TypeId { get; set; }
        public string Level { get; set; }
        public string IfcClass { get; set; }
        public string IfcExportAs { get; set; }
        public string IfcExportType { get; set; }
        public bool MissingIfcClass { get; set; }
        public bool MissingIfcExportAs { get; set; }
        public bool MissingIfcExportType { get; set; }
        public Dictionary<string, string> InstanceParameters { get; set; }
        public Dictionary<string, string> TypeParameters { get; set; }

        public string Status
        {
            get
            {
                if (MissingIfcClass && MissingIfcExportType) return "Missing IFC class/type";
                if (MissingIfcClass) return "Missing IFC Class";
                if (MissingIfcExportAs) return "Missing IfcExportAs";
                if (MissingIfcExportType) return "Missing IfcExportType";
                return "OK";
            }
        }
    }

    public class CategoryOption
    {
        public int BuiltInCategoryId { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class ReportSaveResult
    {
        public string CsvPath { get; set; }
        public string JsonPath { get; set; }
    }

    public class IfcExportResult
    {
        public bool Success { get; set; }
        public string IfcPath { get; set; }
        public string CsvPath { get; set; }
        public string JsonPath { get; set; }
        public string MissingCheckCsvPath { get; set; }
        public string MissingCheckJsonPath { get; set; }
        public int ObjectCount { get; set; }
        public int RevitUniqueIdValuesWritten { get; set; }
        public int ExportedIfcObjectCount { get; set; }
        public int SuspectedMissingCount { get; set; }
    }

    public class IfcFileObjectRow
    {
        public int StepId { get; set; }
        public string IfcClass { get; set; }
        public string GlobalId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ObjectType { get; set; }
        public string PredefinedType { get; set; }
        public string Status { get; set; }
    }

    public class IfcClassCountRow
    {
        public string IfcClass { get; set; }
        public int Count { get; set; }
    }

    public class IfcFileReviewResult
    {
        public string IfcPath { get; set; }
        public string CsvPath { get; set; }
        public string JsonPath { get; set; }
        public int TotalEntityCount { get; set; }
        public int ObjectCount { get; set; }
        public int ProxyCount { get; set; }
        public List<IfcClassCountRow> ClassCounts { get; set; }
        public List<IfcFileObjectRow> Objects { get; set; }
    }

    public class IfcMissingCheckRow
    {
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string ExpectedIfcClass { get; set; }
        public int RevitCount { get; set; }
        public int IfcClassCount { get; set; }
        public int SuspectedMissingCount { get; set; }
        public string Status { get; set; }
    }

    public class IfcModelCompareRow
    {
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string ExpectedIfcClass { get; set; }
        public int RevitCount { get; set; }
        public int IfcMatchedCount { get; set; }
        public int Difference { get; set; }
        public string MatchMethod { get; set; }
        public string Status { get; set; }
    }

    public class IfcModelCompareResult
    {
        public string IfcPath { get; set; }
        public string CsvPath { get; set; }
        public string JsonPath { get; set; }
        public int RevitObjectCount { get; set; }
        public int IfcObjectCount { get; set; }
        public int DifferenceCount { get; set; }
        public List<IfcModelCompareRow> Rows { get; set; }
    }
}
