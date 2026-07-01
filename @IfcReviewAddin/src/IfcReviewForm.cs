using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace IfcReviewAddin
{
    public class IfcReviewForm : System.Windows.Forms.Form
    {
        private readonly Document _doc;
        private readonly IfcReviewService _service;
        private readonly CheckedListBox _categoryList;
        private readonly TextBox _outputFolderText;
        private readonly TextBox _ifcFileNameText;
        private readonly CheckBox _includeParametersCheck;
        private readonly Label _summaryLabel;
        private readonly DataGridView _grid;
        private readonly Dictionary<string, string> _gridFilters;
        private BindingList<IfcReviewRow> _rows;
        private BindingList<IfcFileObjectRow> _ifcFileRows;
        private BindingList<IfcModelCompareRow> _compareRows;
        private string _currentGridKind;
        private const string BlankFilterValue = "__BLANK__";

        public IfcReviewForm(Document doc)
        {
            _doc = doc;
            _service = new IfcReviewService();
            _rows = new BindingList<IfcReviewRow>();
            _ifcFileRows = new BindingList<IfcFileObjectRow>();
            _compareRows = new BindingList<IfcModelCompareRow>();
            _gridFilters = new Dictionary<string, string>();
            _currentGridKind = "model";

            Text = "IFC Export Target Review";
            Width = 1120;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 620);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(root);

            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.ColumnCount = 3;
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            top.Padding = new Padding(10);
            root.Controls.Add(top, 0, 0);

            _categoryList = new CheckedListBox();
            _categoryList.Dock = DockStyle.Fill;
            _categoryList.CheckOnClick = true;
            foreach (CategoryOption option in _service.GetSelectableCategories(_doc))
                _categoryList.Items.Add(option, option.CheckedByDefault);
            top.Controls.Add(_categoryList, 0, 0);

            var settings = new TableLayoutPanel();
            settings.Dock = DockStyle.Fill;
            settings.ColumnCount = 3;
            settings.RowCount = 4;
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settings.Padding = new Padding(8, 0, 8, 0);
            top.Controls.Add(settings, 1, 0);

            settings.Controls.Add(MakeLabel("Output"), 0, 0);
            _outputFolderText = new TextBox();
            _outputFolderText.Dock = DockStyle.Fill;
            _outputFolderText.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "ifc_review_reports");
            settings.Controls.Add(_outputFolderText, 1, 0);

            var browseButton = new Button();
            browseButton.Text = "Browse";
            browseButton.Dock = DockStyle.Fill;
            browseButton.Click += BrowseButton_Click;
            settings.Controls.Add(browseButton, 2, 0);

            settings.Controls.Add(MakeLabel("IFC file"), 0, 1);
            _ifcFileNameText = new TextBox();
            _ifcFileNameText.Dock = DockStyle.Fill;
            _ifcFileNameText.Text = SafeFileName(_doc.Title) + "_electrical.ifc";
            settings.Controls.Add(_ifcFileNameText, 1, 1);

            _includeParametersCheck = new CheckBox();
            _includeParametersCheck.Text = "Include instance/type parameters in detailed JSON";
            _includeParametersCheck.Checked = true;
            _includeParametersCheck.Dock = DockStyle.Fill;
            settings.Controls.Add(_includeParametersCheck, 1, 2);

            _summaryLabel = new Label();
            _summaryLabel.Dock = DockStyle.Fill;
            _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            _summaryLabel.Text = "Run review.";
            settings.Controls.Add(_summaryLabel, 1, 3);
            settings.SetColumnSpan(_summaryLabel, 2);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.TopDown;
            actions.WrapContents = false;
            actions.AutoScroll = true;
            actions.Padding = new Padding(8, 0, 0, 0);
            top.Controls.Add(actions, 2, 0);

            actions.Controls.Add(MakeActionButton("Review Existing IFC", ReviewExistingIfcButton_Click));
            actions.Controls.Add(MakeActionButton("Compare Model vs IFC", CompareModelIfcButton_Click));
            actions.Controls.Add(MakeActionButton("Open Output Folder", OpenFolderButton_Click));
            actions.Controls.Add(MakeActionButton("Review Model", ReviewButton_Click));

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.CellMouseDown += Grid_CellMouseDown;
            _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            _grid.KeyDown += Grid_KeyDown;
            _grid.DataSource = _rows;
            ConfigureModelGridColumns();
            root.Controls.Add(_grid, 0, 1);

            var footer = new Label();
            footer.Dock = DockStyle.Fill;
            footer.TextAlign = ContentAlignment.MiddleLeft;
            footer.Padding = new Padding(10, 0, 0, 0);
            footer.Text = "CSV is for table review. JSON contains detailed family, type, and instance parameter data.";
            root.Controls.Add(footer, 0, 2);
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _outputFolderText.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _outputFolderText.Text = dialog.SelectedPath;
            }
        }

        private void ReviewButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<int> categories = GetSelectedCategoryIds();
                List<IfcReviewRow> rows = _service.CollectRows(_doc, categories, _includeParametersCheck.Checked);
                _rows = new BindingList<IfcReviewRow>(rows);
                SetCurrentGrid("model");
                ConfigureModelGridColumns();
                ApplyGridFilters();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveReportButton_Click(object sender, EventArgs e)
        {
            try
            {
                EnsureRows();
                ReportSaveResult result = _service.SaveReport(_doc, _outputFolderText.Text, _rows.ToList());
                MessageBox.Show(this, "Saved.\n\nCSV: " + result.CsvPath + "\nJSON: " + result.JsonPath, "IFC Review");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportIfcButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<int> categories = GetSelectedCategoryIds();
                IfcExportResult result = _service.ExportIfcWithReport(
                    _doc,
                    _outputFolderText.Text,
                    _ifcFileNameText.Text,
                    categories,
                    _includeParametersCheck.Checked);

                MessageBox.Show(
                    this,
                    "IFC export completed: " + result.Success +
                    "\n\nIFC: " + result.IfcPath +
                    "\nCSV: " + result.CsvPath +
                    "\nJSON: " + result.JsonPath +
                    "\nMissing check CSV: " + result.MissingCheckCsvPath +
                    "\nMissing check JSON: " + result.MissingCheckJsonPath +
                    "\nRevitUniqueId values written: " + result.RevitUniqueIdValuesWritten +
                    "\nRevit target objects: " + result.ObjectCount +
                    "\nIFC file objects: " + result.ExportedIfcObjectCount +
                    "\nSuspected missing: " + result.SuspectedMissingCount,
                    "IFC Review");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReviewExistingIfcButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*";
                    dialog.Title = "Select IFC file to review";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    IfcFileReviewResult result = _service.ReviewExistingIfcFile(dialog.FileName, _outputFolderText.Text);
                    _ifcFileRows = new BindingList<IfcFileObjectRow>(result.Objects);
                    SetCurrentGrid("ifc");
                    ConfigureIfcFileGridColumns();
                    ApplyGridFilters();
                    _summaryLabel.Text = "IFC file objects: " + result.ObjectCount +
                        " / IFC classes: " + result.ClassCounts.Count +
                        " / Proxy objects: " + result.ProxyCount;

                    MessageBox.Show(
                        this,
                        "IFC file review completed." +
                        "\n\nIFC: " + result.IfcPath +
                        "\nCSV: " + result.CsvPath +
                        "\nJSON: " + result.JsonPath +
                        "\nEntities: " + result.TotalEntityCount +
                        "\nObjects: " + result.ObjectCount +
                        "\nProxy objects: " + result.ProxyCount,
                        "IFC Review");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CompareModelIfcButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*";
                    dialog.Title = "Select IFC file to compare with current model";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    IfcModelCompareResult result = _service.CompareCurrentModelWithIfc(
                        _doc,
                        dialog.FileName,
                        _outputFolderText.Text,
                        GetSelectedCategoryIds());

                    _compareRows = new BindingList<IfcModelCompareRow>(result.Rows);
                    SetCurrentGrid("compare");
                    ConfigureCompareGridColumns();
                    ApplyGridFilters();
                    _summaryLabel.Text = "Revit objects: " + result.RevitObjectCount +
                        " / IFC objects: " + result.IfcObjectCount +
                        " / Difference score: " + result.DifferenceCount;

                    MessageBox.Show(
                        this,
                        "Model vs IFC comparison completed." +
                        "\n\nIFC: " + result.IfcPath +
                        "\nCSV: " + result.CsvPath +
                        "\nJSON: " + result.JsonPath +
                        "\nRevit objects: " + result.RevitObjectCount +
                        "\nIFC objects: " + result.IfcObjectCount +
                        "\nDifference score: " + result.DifferenceCount,
                        "IFC Review");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(_outputFolderText.Text))
                    Directory.CreateDirectory(_outputFolderText.Text);
                Process.Start(_outputFolderText.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IFC Review", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            CopyCellValue(e.RowIndex, e.ColumnIndex);
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (e.Button != MouseButtons.Right) return;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy Cell", null, delegate { CopyCellValue(e.RowIndex, e.ColumnIndex); });

            string elementIds = GetRowElementIdText(e.RowIndex);
            if (!string.IsNullOrWhiteSpace(elementIds))
                menu.Items.Add("Copy ElementId(s)", null, delegate { Clipboard.SetText(elementIds); });

            menu.Show(_grid, _grid.PointToClient(Cursor.Position));
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.KeyCode != Keys.C) return;
            if (_grid.CurrentCell == null) return;

            CopyCellValue(_grid.CurrentCell.RowIndex, _grid.CurrentCell.ColumnIndex);
            e.SuppressKeyPress = true;
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;

            DataGridViewColumn column = _grid.Columns[e.ColumnIndex];
            string propertyName = column.DataPropertyName;
            if (string.IsNullOrWhiteSpace(propertyName)) return;

            ShowColumnFilterMenu(column, propertyName);
        }

        private void CopyCellValue(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0) return;
            object value = _grid.Rows[rowIndex].Cells[columnIndex].Value;
            string text = value != null ? value.ToString() : "";
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        private string GetRowElementIdText(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return "";
            object item = _grid.Rows[rowIndex].DataBoundItem;
            if (item == null) return "";

            string diffIds = GetPropertyText(item, "DifferenceElementIds");
            if (!string.IsNullOrWhiteSpace(diffIds)) return diffIds;

            string revitIds = GetPropertyText(item, "RevitElementIds");
            if (!string.IsNullOrWhiteSpace(revitIds)) return revitIds;

            return GetPropertyText(item, "ElementId");
        }

        private void ShowColumnFilterMenu(DataGridViewColumn column, string propertyName)
        {
            var menu = new ContextMenuStrip();

            var allItem = new ToolStripMenuItem("(All)");
            allItem.Checked = !_gridFilters.ContainsKey(propertyName);
            allItem.Click += delegate
            {
                _gridFilters.Remove(propertyName);
                ApplyGridFilters();
            };
            menu.Items.Add(allItem);

            var blankItem = new ToolStripMenuItem("(Blanks)");
            blankItem.Checked = _gridFilters.ContainsKey(propertyName) && _gridFilters[propertyName] == BlankFilterValue;
            blankItem.Click += delegate
            {
                _gridFilters[propertyName] = BlankFilterValue;
                ApplyGridFilters();
            };
            menu.Items.Add(blankItem);
            menu.Items.Add(new ToolStripSeparator());

            List<string> values = GetCurrentSourceItems()
                .Select(item => GetPropertyText(item, propertyName))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .Take(200)
                .ToList();

            foreach (string value in values)
            {
                string filterValue = value;
                var item = new ToolStripMenuItem(value);
                item.Checked = _gridFilters.ContainsKey(propertyName) &&
                    string.Equals(_gridFilters[propertyName], filterValue, StringComparison.OrdinalIgnoreCase);
                item.Click += delegate
                {
                    _gridFilters[propertyName] = filterValue;
                    ApplyGridFilters();
                };
                menu.Items.Add(item);
            }

            menu.Show(_grid, _grid.GetCellDisplayRectangle(column.Index, -1, true).Left, _grid.ColumnHeadersHeight);
        }

        private void SetCurrentGrid(string kind)
        {
            _currentGridKind = kind;
            _gridFilters.Clear();
        }

        private void ApplyGridFilters()
        {
            if (_currentGridKind == "ifc")
                _grid.DataSource = new BindingList<IfcFileObjectRow>(ApplyFilters(_ifcFileRows.Cast<object>()).Cast<IfcFileObjectRow>().ToList());
            else if (_currentGridKind == "compare")
                _grid.DataSource = new BindingList<IfcModelCompareRow>(ApplyFilters(_compareRows.Cast<object>()).Cast<IfcModelCompareRow>().ToList());
            else
                _grid.DataSource = new BindingList<IfcReviewRow>(ApplyFilters(_rows.Cast<object>()).Cast<IfcReviewRow>().ToList());

            UpdateFilterHeaders();
        }

        private IEnumerable<object> ApplyFilters(IEnumerable<object> source)
        {
            foreach (object item in source)
            {
                bool visible = true;
                foreach (KeyValuePair<string, string> filter in _gridFilters)
                {
                    string value = GetPropertyText(item, filter.Key);
                    if (filter.Value == BlankFilterValue)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            visible = false;
                            break;
                        }
                    }
                    else if (!string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        visible = false;
                        break;
                    }
                }

                if (visible)
                    yield return item;
            }
        }

        private IEnumerable<object> GetCurrentSourceItems()
        {
            if (_currentGridKind == "ifc")
                return _ifcFileRows.Cast<object>();
            if (_currentGridKind == "compare")
                return _compareRows.Cast<object>();
            return _rows.Cast<object>();
        }

        private void UpdateFilterHeaders()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                string header = Convert.ToString(column.Tag);
                if (string.IsNullOrWhiteSpace(header))
                    header = column.HeaderText;

                column.HeaderText = _gridFilters.ContainsKey(column.DataPropertyName)
                    ? header + " *"
                    : header;
            }
        }

        private static string GetPropertyText(object item, string propertyName)
        {
            if (item == null || string.IsNullOrWhiteSpace(propertyName)) return "";

            PropertyDescriptor property = TypeDescriptor.GetProperties(item)[propertyName];
            if (property == null) return "";

            object value = property.GetValue(item);
            return value != null ? value.ToString() : "";
        }

        private void ConfigureModelGridColumns()
        {
            _grid.Columns.Clear();
            AddColumn("ElementId", "ElementId", 78);
            AddColumn("IfcGlobalId", "IFC GlobalId", 150);
            AddColumn("Category", "Category", 120);
            AddColumn("Family", "Family", 220);
            AddColumn("Type", "Type", 220);
            AddColumn("Level", "Level", 90);
            AddColumn("IfcClass", "IFC Class", 140);
            AddColumn("IfcExportAs", "IfcExportAs", 140);
            AddColumn("IfcExportType", "IfcExportType", 140);
            AddColumn("Status", "Status", 150);
        }

        private void ConfigureIfcFileGridColumns()
        {
            _grid.Columns.Clear();
            AddColumn("StepId", "StepId", 70);
            AddColumn("IfcClass", "IFC Class", 190);
            AddColumn("Name", "Name", 220);
            AddColumn("ObjectType", "ObjectType", 180);
            AddColumn("PredefinedType", "PredefinedType", 130);
            AddColumn("RevitCategory", "Revit Category", 130);
            AddColumn("RevitFamily", "Revit Family", 160);
            AddColumn("RevitType", "Revit Type", 160);
            AddColumn("Status", "Status", 150);
        }

        private void ConfigureCompareGridColumns()
        {
            _grid.Columns.Clear();
            AddColumn("Category", "Category", 120);
            AddColumn("Family", "Family", 210);
            AddColumn("Type", "Type", 210);
            AddColumn("ExpectedIfcClass", "Expected IFC Class", 170);
            AddColumn("RevitCount", "Revit Count", 90);
            AddColumn("IfcMatchedCount", "IFC Matched", 95);
            AddColumn("Difference", "Difference", 85);
            AddColumn("DifferenceElementIds", "Difference ElementIds", 180);
            AddColumn("RevitElementIds", "All Revit ElementIds", 220);
            AddColumn("MatchMethod", "Match Method", 170);
            AddColumn("Status", "Status", 130);
        }

        private void AddColumn(string propertyName, string headerText, int width)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = propertyName,
                HeaderText = headerText,
                Tag = headerText,
                Width = width
            });
        }

        private List<int> GetSelectedCategoryIds()
        {
            var ids = new List<int>();
            foreach (object item in _categoryList.CheckedItems)
            {
                CategoryOption option = item as CategoryOption;
                if (option != null)
                    ids.Add(option.BuiltInCategoryId);
            }
            if (ids.Count == 0)
                throw new InvalidOperationException("Select at least one category.");
            return ids;
        }

        private void EnsureRows()
        {
            if (_rows == null || _rows.Count == 0)
                ReviewButton_Click(this, EventArgs.Empty);
        }

        private void UpdateSummary()
        {
            int total = _rows.Count;
            int missingClass = _rows.Count(r => r.MissingIfcClass);
            int missingAs = _rows.Count(r => r.MissingIfcExportAs);
            int missingType = _rows.Count(r => r.MissingIfcExportType);
            int typeCount = _rows
                .Select(r => r.Category + "|" + r.Family + "|" + r.Type)
                .Distinct()
                .Count();

            _summaryLabel.Text = "Objects: " + total + " / Types: " + typeCount +
                " / Missing IFC Class: " + missingClass +
                " / Missing IfcExportAs: " + missingAs +
                " / Missing IfcExportType: " + missingType;
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button MakeActionButton(string text, EventHandler handler)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 320;
            button.Height = 28;
            button.Margin = new Padding(0, 0, 0, 6);
            button.Click += handler;
            return button;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "revit_model";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }
    }
}
