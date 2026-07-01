using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public class ExportOptions
    {
        public string OutputPath { get; set; }
        public string SheetName { get; set; }
        public IList<Category> Categories { get; set; }
        public IList<ParameterSelection> Parameters { get; set; }
    }
}
