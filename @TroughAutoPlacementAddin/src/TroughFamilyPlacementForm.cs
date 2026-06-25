using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using DrawingColor = System.Drawing.Color;

namespace TroughAutoPlacementAddin
{
    public class TroughFamilyPlacementForm : System.Windows.Forms.Form
    {
        private const string SettingsFileName = "TroughFamilyPlacementForm.settings";
        private static readonly DrawingColor WindowBackColor = DrawingColor.FromArgb(245, 247, 250);
        private static readonly DrawingColor PanelBackColor = DrawingColor.White;
        private static readonly DrawingColor PrimaryColor = DrawingColor.FromArgb(14, 116, 144);
        private static readonly DrawingColor BorderColor = DrawingColor.FromArgb(210, 216, 226);
        private static readonly DrawingColor LabelColor = DrawingColor.FromArgb(55, 65, 81);

        private readonly Document _document;
        private readonly ComboBox _symbolComboBox = new ComboBox();
        private readonly NumericUpDown _spacingBox = new NumericUpDown();
        private readonly NumericUpDown _toleranceBox = new NumericUpDown();
        private readonly ComboBox _rotationModeComboBox = new ComboBox();
        private readonly NumericUpDown _perpendicularOffsetBox = new NumericUpDown();
        private readonly NumericUpDown _parallelOffsetBox = new NumericUpDown();

        public TroughFamilyPlacementForm(Document document)
        {
            _document = document;
            Settings = new FamilyPlacementSettings();
            InitializeComponent();
            LoadFamilySymbols();
            LoadSavedSettings();
        }

        public FamilyPlacementSettings Settings { get; private set; }

        private void InitializeComponent()
        {
            Text = "\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            BackColor = WindowBackColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(500, 420);
            Size = new Size(500, 420);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            var titleLabel = new Label();
            titleLabel.Text = "\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            titleLabel.ForeColor = DrawingColor.FromArgb(17, 24, 39);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(titleLabel, 0, 0);

            var fieldsPanel = new System.Windows.Forms.Panel();
            fieldsPanel.Dock = DockStyle.Fill;
            fieldsPanel.BackColor = PanelBackColor;
            fieldsPanel.Padding = new Padding(18, 12, 18, 12);
            root.Controls.Add(fieldsPanel, 0, 1);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            fieldsPanel.Controls.Add(layout);

            AddLabel(layout, "Family type", 0);
            ConfigureComboBox(_symbolComboBox);
            _symbolComboBox.DropDown += SymbolComboBox_DropDown;
            layout.Controls.Add(_symbolComboBox, 1, 0);

            AddNumberRow(layout, "Spacing (mm)", _spacingBox, 1, 1, 1000000, 500);
            AddNumberRow(layout, "Join tolerance (mm)", _toleranceBox, 2, 0, 1000000, 50);

            AddLabel(layout, "Rotation mode", 3);
            ConfigureComboBox(_rotationModeComboBox);
            _rotationModeComboBox.Items.Add("0 - Main edge direction");
            _rotationModeComboBox.Items.Add("1 - Side direction");
            _rotationModeComboBox.Items.Add("2 - Reverse main direction");
            _rotationModeComboBox.SelectedIndex = 1;
            layout.Controls.Add(_rotationModeComboBox, 1, 3);

            AddNumberRow(layout, "X offset (mm)", _perpendicularOffsetBox, 4, -1000000, 1000000, 0);
            AddNumberRow(layout, "Y offset (mm)", _parallelOffsetBox, 5, -1000000, 1000000, 400);

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.WrapContents = false;
            buttonPanel.Padding = new Padding(0, 12, 0, 0);
            buttonPanel.BackColor = WindowBackColor;
            root.Controls.Add(buttonPanel, 0, 2);

            var okButton = CreateButton("Place", PrimaryColor, DrawingColor.White);
            okButton.Click += OkButton_Click;

            var cancelButton = CreateButton("Cancel", DrawingColor.White, LabelColor);
            cancelButton.DialogResult = DialogResult.Cancel;

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void LoadFamilySymbols()
        {
            var symbols = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => symbol.Category != null)
                .OrderBy(symbol => symbol.Category.Name)
                .ThenBy(symbol => symbol.Family == null ? string.Empty : symbol.Family.Name)
                .ThenBy(symbol => symbol.Name)
                .ToList();

            foreach (FamilySymbol symbol in symbols)
                _symbolComboBox.Items.Add(new FamilySymbolListItem(symbol));

            UpdateDropDownWidth(_symbolComboBox);

            if (_symbolComboBox.Items.Count > 0)
                _symbolComboBox.SelectedIndex = 0;
        }

        private void SymbolComboBox_DropDown(object sender, EventArgs e)
        {
            UpdateDropDownWidth(_symbolComboBox);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (!TryUpdateSettings())
                return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private bool TryUpdateSettings()
        {
            var selectedSymbol = _symbolComboBox.SelectedItem as FamilySymbolListItem;
            if (selectedSymbol == null)
            {
                MessageBox.Show(this, "Select a family type.", "\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58");
                return false;
            }

            if (_spacingBox.Value <= 0)
            {
                MessageBox.Show(this, "Spacing must be greater than 0.", "\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58");
                return false;
            }

            Settings = new FamilyPlacementSettings
            {
                FamilySymbolId = selectedSymbol.Symbol.Id,
                SpacingMm = (double)_spacingBox.Value,
                ToleranceMm = (double)_toleranceBox.Value,
                RotationMode = _rotationModeComboBox.SelectedIndex,
                PerpendicularOffsetMm = (double)_perpendicularOffsetBox.Value,
                ParallelOffsetMm = (double)_parallelOffsetBox.Value
            };

            SaveSettings(selectedSymbol);
            return true;
        }

        private void LoadSavedSettings()
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return;

            Dictionary<string, string> values = ReadSettingsFile(path);
            SetNumberValue(_spacingBox, GetDouble(values, "SpacingMm", (double)_spacingBox.Value));
            SetNumberValue(_toleranceBox, GetDouble(values, "ToleranceMm", (double)_toleranceBox.Value));
            SetNumberValue(_perpendicularOffsetBox, GetDouble(values, "PerpendicularOffsetMm", (double)_perpendicularOffsetBox.Value));
            SetNumberValue(_parallelOffsetBox, GetDouble(values, "ParallelOffsetMm", (double)_parallelOffsetBox.Value));
            _rotationModeComboBox.SelectedIndex = Math.Max(0, Math.Min(2, (int)GetDouble(values, "RotationMode", _rotationModeComboBox.SelectedIndex)));

            string familySymbolUniqueId;
            if (values.TryGetValue("FamilySymbolUniqueId", out familySymbolUniqueId))
                SelectFamilySymbol(familySymbolUniqueId);
        }

        private void SaveSettings(FamilySymbolListItem selectedSymbol)
        {
            var lines = new List<string>();
            lines.Add("FamilySymbolUniqueId=" + selectedSymbol.Symbol.UniqueId);
            lines.Add("SpacingMm=" + FormatDouble(Settings.SpacingMm));
            lines.Add("ToleranceMm=" + FormatDouble(Settings.ToleranceMm));
            lines.Add("RotationMode=" + Settings.RotationMode.ToString(CultureInfo.InvariantCulture));
            lines.Add("PerpendicularOffsetMm=" + FormatDouble(Settings.PerpendicularOffsetMm));
            lines.Add("ParallelOffsetMm=" + FormatDouble(Settings.ParallelOffsetMm));
            File.WriteAllLines(GetSettingsPath(), lines.ToArray());
        }

        private void SelectFamilySymbol(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return;

            for (int i = 0; i < _symbolComboBox.Items.Count; i++)
            {
                var item = _symbolComboBox.Items[i] as FamilySymbolListItem;
                if (item != null && item.Symbol.UniqueId == uniqueId)
                {
                    _symbolComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string GetSettingsPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins",
                "2024",
                "TroughAutoPlacementAddin");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return Path.Combine(folder, SettingsFileName);
        }

        private static Dictionary<string, string> ReadSettingsFile(string path)
        {
            var result = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                result[line.Substring(0, separatorIndex)] = line.Substring(separatorIndex + 1);
            }

            return result;
        }

        private static double GetDouble(Dictionary<string, string> values, string key, double fallback)
        {
            string rawValue;
            if (!values.TryGetValue(key, out rawValue))
                return fallback;

            double parsed;
            return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void SetNumberValue(NumericUpDown box, double value)
        {
            decimal decimalValue = (decimal)value;
            if (decimalValue < box.Minimum)
                decimalValue = box.Minimum;
            if (decimalValue > box.Maximum)
                decimalValue = box.Maximum;
            box.Value = decimalValue;
        }

        private static void ConfigureComboBox(ComboBox comboBox)
        {
            comboBox.Dock = DockStyle.None;
            comboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            comboBox.Height = 24;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = DrawingColor.White;
            comboBox.ForeColor = LabelColor;
            comboBox.Margin = new Padding(0, 5, 0, 0);
        }

        private static void UpdateDropDownWidth(ComboBox comboBox)
        {
            int widestItemWidth = comboBox.Width;

            foreach (object item in comboBox.Items)
            {
                string text = item == null ? string.Empty : item.ToString();
                int itemWidth = TextRenderer.MeasureText(text, comboBox.Font).Width;
                widestItemWidth = Math.Max(widestItemWidth, itemWidth);
            }

            int maxScreenWidth = Screen.FromControl(comboBox).WorkingArea.Width - 40;
            comboBox.DropDownWidth = Math.Min(widestItemWidth + SystemInformation.VerticalScrollBarWidth + 24, maxScreenWidth);
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int row)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = LabelColor;
            label.Font = new Font("Malgun Gothic", 9F, FontStyle.Regular);
            layout.Controls.Add(label, 0, row);
        }

        private static void AddNumberRow(TableLayoutPanel layout, string label, NumericUpDown box, int row, decimal minimum, decimal maximum, decimal value)
        {
            AddLabel(layout, label, row);
            box.Dock = DockStyle.None;
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Height = 24;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.DecimalPlaces = 0;
            box.Increment = 1;
            box.ThousandsSeparator = true;
            box.Value = value;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = DrawingColor.White;
            box.ForeColor = LabelColor;
            box.Margin = new Padding(0, 5, 0, 0);
            layout.Controls.Add(box, 1, row);
        }

        private static Button CreateButton(string text, DrawingColor backColor, DrawingColor foreColor)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 92;
            button.Height = 34;
            button.Margin = new Padding(8, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            button.UseVisualStyleBackColor = false;
            return button;
        }
    }
}
