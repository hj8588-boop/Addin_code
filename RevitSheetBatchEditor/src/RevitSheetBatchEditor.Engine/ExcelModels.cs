using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace RevitSheetBatchEditor.Engine
{
    public sealed class ExcelTable
    {
        public List<string> Headers { get; set; }
        public List<List<string>> Rows { get; set; }
    }

    public sealed class ExcelImportValue
    {
        public ElementId SheetId { get; set; }
        public string Source { get; set; }
        public string Name { get; set; }
        public StorageType StorageType { get; set; }
        public string Value { get; set; }
    }

    public sealed class SheetParameterValueRow : INotifyPropertyChanged
    {
        private string _newValue;
        private bool _isModified;
        public ElementId SheetId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string Source { get; set; }
        public string ParameterName { get; set; }
        public StorageType StorageType { get; set; }
        public string CurrentValue { get; set; }
        public bool IsAvailable { get; set; }
        public string Status { get; set; }
        public bool IsModified { get { return _isModified; } }
        public string NewValue
        {
            get { return _newValue; }
            set
            {
                if (_newValue == value) return;
                _newValue = value;
                _isModified = true;
                OnPropertyChanged();
                OnPropertyChanged("IsModified");
            }
        }
        public void SetInitialValue(string value) { _newValue = value; _isModified = false; }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ExcelImportResult
    {
        public List<ExcelImportValue> Values { get; set; }
        public int MatchedSheets { get; set; }
        public int UnmatchedRows { get; set; }
        public int MappedValues { get; set; }
    }

}
