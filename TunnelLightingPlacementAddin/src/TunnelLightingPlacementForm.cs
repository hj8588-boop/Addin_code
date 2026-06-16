using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace TunnelLightingPlacementAddin
{
    public class TunnelLightingPlacementForm : System.Windows.Forms.Form
    {
        private const string SettingsFileName = "TunnelLightingPlacementForm.settings";
        private readonly Document _document;
        private readonly ComboBox _symbolComboBox = new ComboBox();
        private readonly NumericUpDown _startDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _endDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _spacingBox = new NumericUpDown();
        private readonly NumericUpDown _offsetBox = new NumericUpDown();
        private readonly NumericUpDown _heightBox = new NumericUpDown();
        private readonly ComboBox _rotationAngleComboBox = new ComboBox();

        public TunnelLightingPlacementForm(Document document)
        {
            _document = document;
            Settings = new PlacementSettings();
            InitializeComponent();
            LoadFamilySymbols();
            LoadSavedSettings();
            HookPreviewInvalidation();
        }

        public PlacementSettings Settings { get; private set; }
        public bool PreviewIsCurrent { get; private set; }
        public Func<PlacementSettings, int> PreviewPlacement { get; set; }

        private void InitializeComponent()
        {
            Text = "터널 전등 자동배치";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            MinimumSize = new Size(400, 500);
            Size = new Size(400, 500);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.RowCount = 9;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddLabel(layout, "등기구 패밀리 타입", 0);
            ConfigureComboBox(_symbolComboBox);
            layout.Controls.Add(_symbolComboBox, 1, 0);

            AddNumberRow(layout, "시작 거리(mm)", _startDistanceBox, 1, 0, 100000000, 0);
            AddNumberRow(layout, "종료 거리(mm)", _endDistanceBox, 2, 0, 100000000, 0);
            AddNumberRow(layout, "설치 간격(mm)", _spacingBox, 3, 1, 1000000, 1000);
            AddNumberRow(layout, "Offset(mm)", _offsetBox, 4, -1000000, 1000000, 0);
            AddNumberRow(layout, "높이(mm)", _heightBox, 5, -1000000, 1000000, 1200);
            AddLabel(layout, "회전각도", 6);
            ConfigureComboBox(_rotationAngleComboBox);
            _rotationAngleComboBox.Items.Add("0도");
            _rotationAngleComboBox.Items.Add("180도");
            _rotationAngleComboBox.SelectedIndex = 1;
            layout.Controls.Add(_rotationAngleComboBox, 1, 6);

            var note = new Label();
            note.Text = "미리보기를 누르면 현재 설정으로 임시 배치됩니다. 확인은 배치를 확정하고, 취소는 미리보기를 삭제합니다.";
            note.Dock = DockStyle.Fill;
            note.ForeColor = System.Drawing.Color.FromArgb(75, 75, 75);
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.AutoSize = true;
            layout.Controls.Add(note, 0, 7);
            layout.SetColumnSpan(note, 2);

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;

            var okButton = new Button();
            okButton.Text = "확인";
            okButton.Width = 90;
            okButton.Click += OkButton_Click;

            var cancelButton = new Button();
            cancelButton.Text = "취소";
            cancelButton.Width = 90;
            cancelButton.DialogResult = DialogResult.Cancel;

            var previewButton = new Button();
            previewButton.Text = "미리보기";
            previewButton.Width = 90;
            previewButton.Click += PreviewButton_Click;

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(previewButton);
            layout.Controls.Add(buttonPanel, 0, 8);
            layout.SetColumnSpan(buttonPanel, 2);

            Controls.Add(layout);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void LoadFamilySymbols()
        {
            var symbols = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .Cast<FamilySymbol>()
                .OrderBy(s => s.Family == null ? string.Empty : s.Family.Name)
                .ThenBy(s => s.Name)
                .ToList();

            foreach (FamilySymbol symbol in symbols)
                _symbolComboBox.Items.Add(new FamilySymbolListItem(symbol));

            if (_symbolComboBox.Items.Count > 0)
                _symbolComboBox.SelectedIndex = 0;
        }

        private void LoadSavedSettings()
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return;

            Dictionary<string, string> values = ReadSettingsFile(path);
            SetNumberValue(_startDistanceBox, GetDouble(values, "StartDistanceMm", (double)_startDistanceBox.Value));
            SetNumberValue(_endDistanceBox, GetDouble(values, "EndDistanceMm", (double)_endDistanceBox.Value));
            SetNumberValue(_spacingBox, GetDouble(values, "SpacingMm", (double)_spacingBox.Value));
            SetNumberValue(_offsetBox, GetDouble(values, "OffsetMm", (double)_offsetBox.Value));
            SetNumberValue(_heightBox, GetDouble(values, "HeightMm", (double)_heightBox.Value));

            double rotationAngle = GetDouble(values, "RotationAngleDegrees", 180.0);
            _rotationAngleComboBox.SelectedIndex = Math.Abs(rotationAngle) < 1e-8 ? 0 : 1;

            string familySymbolUniqueId;
            if (values.TryGetValue("FamilySymbolUniqueId", out familySymbolUniqueId))
                SelectFamilySymbol(familySymbolUniqueId);
        }

        private void HookPreviewInvalidation()
        {
            _symbolComboBox.SelectedIndexChanged += InputChanged;
            _startDistanceBox.ValueChanged += InputChanged;
            _endDistanceBox.ValueChanged += InputChanged;
            _spacingBox.ValueChanged += InputChanged;
            _offsetBox.ValueChanged += InputChanged;
            _heightBox.ValueChanged += InputChanged;
            _rotationAngleComboBox.SelectedIndexChanged += InputChanged;
        }

        private void InputChanged(object sender, EventArgs e)
        {
            PreviewIsCurrent = false;
        }

        private void PreviewButton_Click(object sender, EventArgs e)
        {
            if (!TryUpdateSettings())
                return;

            if (PreviewPlacement == null)
                return;

            try
            {
                PreviewPlacement(Settings);
                PreviewIsCurrent = true;
            }
            catch (Exception ex)
            {
                PreviewIsCurrent = false;
                MessageBox.Show(this, ex.Message, "터널 전등 자동배치");
            }
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
                MessageBox.Show(this, "등기구 패밀리 타입을 선택하세요.", "터널 전등 자동배치");
                return false;
            }

            if (_spacingBox.Value <= 0)
            {
                MessageBox.Show(this, "설치 간격은 0보다 커야 합니다.", "터널 전등 자동배치");
                return false;
            }

            Settings = new PlacementSettings
            {
                FamilySymbolId = selectedSymbol.Symbol.Id,
                StartDistanceMm = (double)_startDistanceBox.Value,
                EndDistanceMm = (double)_endDistanceBox.Value,
                SpacingMm = (double)_spacingBox.Value,
                OffsetMm = (double)_offsetBox.Value,
                HeightMm = (double)_heightBox.Value,
                RotationAngleDegrees = GetSelectedRotationAngleDegrees()
            };

            SaveSettings(selectedSymbol);
            return true;
        }

        private void SaveSettings(FamilySymbolListItem selectedSymbol)
        {
            string path = GetSettingsPath();
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var lines = new[]
            {
                "FamilySymbolUniqueId=" + (selectedSymbol == null ? string.Empty : selectedSymbol.Symbol.UniqueId),
                "StartDistanceMm=" + Settings.StartDistanceMm.ToString(CultureInfo.InvariantCulture),
                "EndDistanceMm=" + Settings.EndDistanceMm.ToString(CultureInfo.InvariantCulture),
                "SpacingMm=" + Settings.SpacingMm.ToString(CultureInfo.InvariantCulture),
                "OffsetMm=" + Settings.OffsetMm.ToString(CultureInfo.InvariantCulture),
                "HeightMm=" + Settings.HeightMm.ToString(CultureInfo.InvariantCulture),
                "RotationAngleDegrees=" + Settings.RotationAngleDegrees.ToString(CultureInfo.InvariantCulture)
            };

            File.WriteAllLines(path, lines);
        }

        private void SelectFamilySymbol(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
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

        private double GetSelectedRotationAngleDegrees()
        {
            return _rotationAngleComboBox.SelectedIndex == 0 ? 0.0 : 180.0;
        }

        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Autodesk", "Revit", "Addins", "2024", "TunnelLightingPlacementAddin", SettingsFileName);
        }

        private static Dictionary<string, string> ReadSettingsFile(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                result[key] = value;
            }

            return result;
        }

        private static double GetDouble(Dictionary<string, string> values, string key, double fallback)
        {
            string text;
            double value;
            if (values.TryGetValue(key, out text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            return fallback;
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
            comboBox.Dock = DockStyle.Fill;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int row)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoSize = true;
            layout.Controls.Add(label, 0, row);
        }

        private static void AddNumberRow(
            TableLayoutPanel layout,
            string label,
            NumericUpDown box,
            int row,
            decimal minimum,
            decimal maximum,
            decimal value)
        {
            AddLabel(layout, label, row);
            box.Dock = DockStyle.Fill;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.DecimalPlaces = 0;
            box.Increment = 1;
            box.ThousandsSeparator = true;
            box.Value = value;
            layout.Controls.Add(box, 1, row);
        }
    }
}
