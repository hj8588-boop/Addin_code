using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace RevitSheetBatchEditor.Engine
{
    public sealed class ParameterEditRow : INotifyPropertyChanged
    {
        public const string SourceSheet = "Sheet";
        public const string SourceTitleBlockInstance = "Title Block Instance";
        public const string SourceTitleBlockType = "Title Block Type";
        public const string SourceProjectInformation = "Project Information";

        private bool _isSelected;
        private string _newValue;
        private string _status;

        public string Name { get; set; }
        public string Source { get; set; }
        public StorageType StorageType { get; set; }
        public string TypeName { get; set; }
        public string CurrentValue { get; set; }
        public bool IsSelected { get { return _isSelected; } set { Set(ref _isSelected, value); } }
        public string NewValue { get { return _newValue; } set { Set(ref _newValue, value); } }
        public string Status { get { return _status; } set { Set(ref _status, value); } }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
