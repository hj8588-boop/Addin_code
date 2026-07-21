using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace RevitSheetBatchEditor.Engine
{
    public partial class SheetBatchWindow : Window
    {
        private readonly SheetBatchService _service;
        private readonly ObservableCollection<SheetRow> _rows;
        private readonly ObservableCollection<ParameterEditRow> _parameterEdits;
        private readonly List<ExcelImportValue> _excelImportValues;
        private readonly ObservableCollection<SheetParameterValueRow> _sheetParameterValues;
        private readonly Dictionary<string, SheetParameterValueRow> _sheetParameterEditCache;
        private readonly ICollectionView _view;
        private bool _parameterRefreshQueued;
        private bool _isBulkSheetSelectionChange;

        public SheetBatchWindow(Document document)
        {
            InitializeComponent();
            _service = new SheetBatchService(document);
            _rows = new ObservableCollection<SheetRow>(_service.ReadSheets());
            foreach (SheetRow row in _rows) row.PropertyChanged += SheetRow_PropertyChanged;
            SheetGrid.ItemsSource = _rows;
            _view = CollectionViewSource.GetDefaultView(_rows);
            _parameterEdits = new ObservableCollection<ParameterEditRow>();
            _excelImportValues = new List<ExcelImportValue>();
            _sheetParameterValues = new ObservableCollection<SheetParameterValueRow>();
            _sheetParameterEditCache = new Dictionary<string, SheetParameterValueRow>();
            ParameterGrid.ItemsSource = _parameterEdits;
            SheetParameterGrid.ItemsSource = _sheetParameterValues;
            RefreshParameters();
            UpdateCount();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetBulkSheetSelection(_view.Cast<SheetRow>(), true);
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            SetBulkSheetSelection(_view.Cast<SheetRow>(), false);
        }

        private void SetBulkSheetSelection(IEnumerable<SheetRow> rows, bool isSelected)
        {
            _isBulkSheetSelectionChange = true;
            try
            {
                foreach (SheetRow row in rows.ToList()) row.IsSelected = isSelected;
            }
            finally
            {
                _isBulkSheetSelectionChange = false;
            }
            RefreshParameters();
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            SheetGrid.CommitEdit();
            if (!_rows.Any(x => x.IsSelected))
            {
                MessageText.Text = "Select at least one sheet to export.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = "Revit_Sheet_Parameters_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                // 화면 표와 Excel이 항상 같은 최신 선택 시트를 기준으로 조회하게 합니다.
                RefreshParameters();
                ExcelTable table = _service.BuildExcelTable(_rows);
                SimpleXlsxService.Write(dialog.FileName, table);
                MessageText.Text = "Exported " + table.Rows.Count + " selected sheets to Excel.";
            }
            catch (Exception ex)
            {
                MessageText.Text = "Excel export failed: " + ex.Message;
            }
        }

        private void ImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                ExcelTable table = SimpleXlsxService.Read(dialog.FileName);
                ExcelImportResult result = _service.StageExcelImport(table, _rows);
                _excelImportValues.Clear();
                _excelImportValues.AddRange(result.Values);
                RefreshParameters();
                PreviewExcelImportValues(result.Values);
                SheetGrid.Items.Refresh();
                MessageText.Text = "Excel mapping complete: " + result.MatchedSheets + " sheets, "
                    + result.MappedValues + " parameter values, " + result.UnmatchedRows
                    + " unmatched rows. Review the new values below, then click Apply.";
            }
            catch (Exception ex)
            {
                _excelImportValues.Clear();
                MessageText.Text = "Excel import failed: " + ex.Message;
            }
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string keyword = SearchBox.Text == null ? null : SearchBox.Text.Trim();
            _view.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(keyword)) return true;
                var row = (SheetRow)item;
                return Contains(row.CurrentNumber, keyword) || Contains(row.NewNumber, keyword)
                    || Contains(row.CurrentName, keyword) || Contains(row.NewName, keyword)
                    || Contains(row.TitleBlockName, keyword);
            };
            UpdateCount();
        }

        private void SheetRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsSelected" || _parameterRefreshQueued || _isBulkSheetSelectionChange) return;
            _parameterRefreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _parameterRefreshQueued = false;
                RefreshParameters();
            }), DispatcherPriority.Background);
        }

        private static bool Contains(string source, string keyword)
        {
            return source != null && source.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void GenerateNumbers_Click(object sender, RoutedEventArgs e)
        {
            int startNumber;
            int digits;
            int increment;
            if (!int.TryParse(StartNumberBox.Text, out startNumber))
            {
                MessageText.Text = "Enter an integer for Start.";
                return;
            }
            if (!int.TryParse(NumberDigitsBox.Text, out digits) || digits < 1 || digits > 10)
            {
                MessageText.Text = "Digits must be between 1 and 10.";
                return;
            }
            if (!int.TryParse(NumberIncrementBox.Text, out increment) || increment == 0)
            {
                MessageText.Text = "Increment must be a non-zero integer.";
                return;
            }

            SheetGrid.CommitEdit();
            var targets = _view.Cast<SheetRow>().Where(x => x.IsSelected).ToList();
            if (targets.Count == 0)
            {
                MessageText.Text = "Select sheets before generating numbers.";
                return;
            }

            string prefix = NumberPrefixBox.Text ?? string.Empty;
            string suffix = NumberSuffixBox.Text ?? string.Empty;
            string format = "D" + digits;
            for (int index = 0; index < targets.Count; index++)
            {
                int number;
                try { number = checked(startNumber + (index * increment)); }
                catch (OverflowException)
                {
                    MessageText.Text = "The generated number exceeds the integer range.";
                    return;
                }
                targets[index].NewNumber = prefix + number.ToString(format) + suffix;
            }

            string validation = _service.Validate(_rows);
            MessageText.Text = validation ?? "Generated a number preview for " + targets.Count + " selected sheets.";
        }

        private void ResetNumbers_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;
            foreach (SheetRow row in _view.Cast<SheetRow>().Where(x => x.IsSelected))
            {
                row.NewNumber = row.CurrentNumber;
                count++;
            }
            MessageText.Text = "Restored the original numbers for " + count + " selected sheets.";
        }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            SheetGrid.CommitEdit();
            ParameterGrid.CommitEdit();
            string message = _service.Validate(_rows);
            if (message == null) message = _service.ValidateParameters(_parameterEdits);
            MessageText.Text = message ?? "Validation complete. The changes can be applied.";
        }

        private void RefreshParameters_Click(object sender, RoutedEventArgs e)
        {
            RefreshParameters();
        }

        private void RefreshParameters()
        {
            ParameterEditRow previouslySelected = ParameterGrid.SelectedItem as ParameterEditRow;
            string selectedSource = previouslySelected == null ? null : previouslySelected.Source;
            string selectedName = previouslySelected == null ? null : previouslySelected.Name;
            StorageType? selectedStorageType = previouslySelected == null
                ? (StorageType?)null : previouslySelected.StorageType;

            _sheetParameterValues.Clear();
            _parameterEdits.Clear();
            foreach (ParameterEditRow edit in _service.ReadWritableParameters(_rows))
                _parameterEdits.Add(edit);

            ParameterEditRow parameterToSelect = _parameterEdits.FirstOrDefault(x =>
                string.Equals(x.Source, selectedSource, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(x.Name, selectedName, StringComparison.CurrentCultureIgnoreCase)
                && x.StorageType == selectedStorageType);
            ParameterGrid.SelectedItem = parameterToSelect ?? _parameterEdits.FirstOrDefault();

            MessageText.Text = _parameterEdits.Count == 0
                ? "No editable sheet or title block parameters were found on the selected sheets."
                : "Loaded " + _parameterEdits.Count + " sheet/title block parameters and their current values.";
        }

        private void ParameterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ParameterEditRow selectedParameter = ParameterGrid.SelectedItem as ParameterEditRow;
            _sheetParameterValues.Clear();
            if (selectedParameter == null) return;

            foreach (SheetParameterValueRow loaded in _service.ReadSheetParameterValues(selectedParameter, _rows))
            {
                string key = GetSheetParameterKey(loaded);
                SheetParameterValueRow cached;
                if (_sheetParameterEditCache.TryGetValue(key, out cached) && cached.IsModified)
                    _sheetParameterValues.Add(cached);
                else
                {
                    _sheetParameterEditCache[key] = loaded;
                    _sheetParameterValues.Add(loaded);
                }
            }
        }

        private static string GetSheetParameterKey(SheetParameterValueRow row)
        {
            return row.SheetId.Value + "|" + row.Source + "|" + row.ParameterName + "|" + row.StorageType;
        }

        private void PreviewExcelImportValues(IEnumerable<ExcelImportValue> importedValues)
        {
            foreach (ExcelImportValue imported in importedValues)
            {
                ParameterEditRow edit = _parameterEdits.FirstOrDefault(x =>
                    string.Equals(x.Source, imported.Source, StringComparison.CurrentCultureIgnoreCase)
                    && string.Equals(x.Name, imported.Name, StringComparison.CurrentCultureIgnoreCase)
                    && x.StorageType == imported.StorageType);
                SheetRow sheet = _rows.FirstOrDefault(x => x.SheetId == imported.SheetId);
                if (edit == null || sheet == null) continue;

                SheetParameterValueRow valueRow = _service.ReadSheetParameterValues(edit, new[] { sheet })
                    .FirstOrDefault();
                if (valueRow == null || !valueRow.IsAvailable) continue;

                valueRow.NewValue = imported.Value;
                _sheetParameterEditCache[GetSheetParameterKey(valueRow)] = valueRow;
            }

            ParameterGrid_SelectionChanged(ParameterGrid, null);
        }

        private List<ExcelImportValue> BuildIndividualParameterEdits()
        {
            var combined = new Dictionary<string, ExcelImportValue>();
            foreach (ExcelImportValue value in _excelImportValues)
            {
                string key = value.SheetId.Value + "|" + value.Source + "|" + value.Name + "|" + value.StorageType;
                combined[key] = value;
            }
            foreach (SheetParameterValueRow row in _sheetParameterEditCache.Values.Where(x => x.IsAvailable
                && (x.IsModified || !string.Equals(x.NewValue, x.CurrentValue, StringComparison.Ordinal))))
            {
                string key = row.SheetId.Value + "|" + row.Source + "|" + row.ParameterName + "|" + row.StorageType;
                combined[key] = new ExcelImportValue
                {
                    SheetId = row.SheetId,
                    Source = row.Source,
                    Name = row.ParameterName,
                    StorageType = row.StorageType,
                    Value = row.NewValue
                };
            }
            return combined.Values.ToList();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            SheetGrid.CommitEdit();
            ParameterGrid.CommitEdit();
            SheetParameterGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            SheetParameterGrid.CommitEdit(DataGridEditingUnit.Row, true);
            string validation = _service.Validate(_rows);
            if (validation == null) validation = _service.ValidateParameters(_parameterEdits);
            if (validation != null)
            {
                MessageText.Text = validation;
                return;
            }

            try
            {
                _service.Apply(_rows, _parameterEdits, BuildIndividualParameterEdits());
                TaskDialog.Show("Sheet Batch Editor", "Changes were applied to the selected sheets.");
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageText.Text = "Apply failed: " + ex.Message;
            }
        }

        private void UpdateCount()
        {
            CountText.Text = string.Format("Showing {0} of {1} sheets", _view.Cast<object>().Count(), _rows.Count);
        }
    }
}
