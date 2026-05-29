using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public class ImportOptions
    {
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public IList<Category> Categories { get; set; }
        public IList<ParameterSelection> Parameters { get; set; }
    }
}
