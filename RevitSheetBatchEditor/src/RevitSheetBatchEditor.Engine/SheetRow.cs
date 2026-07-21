using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace RevitSheetBatchEditor.Engine
{
    public sealed class SheetRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _newNumber;
        private string _newName;
        private string _status;

        public ElementId SheetId { get; set; }
        public ElementId TitleBlockId { get; set; }
        public string CurrentNumber { get; set; }
        public string CurrentName { get; set; }
        public string TitleBlockName { get; set; }

        public bool IsSelected { get { return _isSelected; } set { Set(ref _isSelected, value); } }
        public string NewNumber { get { return _newNumber; } set { Set(ref _newNumber, value); } }
        public string NewName { get { return _newName; } set { Set(ref _newName, value); } }
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
