using System.Collections.Generic;

namespace SharedParameterValuesExportAddin
{
    public class ImportResult
    {
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int UpdatedElementCount { get; set; }
        public int UpdatedValueCount { get; set; }
        public IList<string> Skipped { get; set; }
        public IList<string> Failed { get; set; }
    }
}
