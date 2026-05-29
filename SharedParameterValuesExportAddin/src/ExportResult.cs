using System.Collections.Generic;

namespace SharedParameterValuesExportAddin
{
    public class ExportResult
    {
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int CategoryCount { get; set; }
        public int ElementCount { get; set; }
        public int ParameterCount { get; set; }
        public IList<string> Parameters { get; set; }
    }
}
