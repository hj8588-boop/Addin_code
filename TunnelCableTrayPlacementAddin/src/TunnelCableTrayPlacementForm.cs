using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using DrawingColor = System.Drawing.Color;
using FormsPanel = System.Windows.Forms.Panel;

namespace TunnelCableTrayPlacementAddin
{
    public class TunnelCableTrayPlacementForm : System.Windows.Forms.Form
    {
        private const string SettingsFileName = "TunnelCableTrayPlacementForm.settings";
        private static readonly DrawingColor WindowBackColor = DrawingColor.FromArgb(245, 247, 250);
        private static readonly DrawingColor PanelBackColor = DrawingColor.White;
        private static readonly DrawingColor PrimaryColor = DrawingColor.FromArgb(14, 116, 144);
        private static readonly DrawingColor BorderColor = DrawingColor.FromArgb(210, 216, 226);
        private static readonly DrawingColor LabelColor = DrawingColor.FromArgb(55, 65, 81);

        private readonly Document _document;
        private readonly ComboBox _typeComboBox = new ComboBox();
        private readonly NumericUpDown _startDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _endDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _segmentLengthBox = new NumericUpDown();
        private readonly NumericUpDown _offsetBox = new NumericUpDown();
        private readonly NumericUpDown _elevationBox = new NumericUpDown();
        private readonly NumericUpDown _widthBox = new NumericUpDown();
        private readonly NumericUpDown _heightBox = new NumericUpDown();

        public TunnelCableTrayPlacementForm(Document document)
        {
            _document = document;
            Settings = new PlacementSettings();
            InitializeComponent();
            LoadCableTrayTypes();
            LoadSavedSettings();
            HookPreviewInvalidation();
        }

        public PlacementSettings Settings { get; private set; }
        public bool PreviewIsCurrent { get; private set; }
        public Func<PlacementSettings, int> PreviewPlacement { get; set; }

        private void InitializeComponent()
        {
            Text = "터널 케이블 트레이 자동배치";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            BackColor = WindowBackColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(430, 480);
            Size = new Size(430, 480);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 300F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            var titleLabel = new Label();
            titleLabel.Text = "터널 케이블 트레이 자동배치";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            titleLabel.ForeColor = DrawingColor.FromArgb(17, 24, 39);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(titleLabel, 0, 0);

            var fieldsPanel = new FormsPanel();
            fieldsPanel.Dock = DockStyle.Fill;
            fieldsPanel.BackColor = PanelBackColor;
            fieldsPanel.Padding = new Padding(18, 12, 18, 12);
            root.Controls.Add(fieldsPanel, 0, 1);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 8;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 8; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            fieldsPanel.Controls.Add(layout);

            AddLabel(layout, "트레이 타입", 0);
            ConfigureComboBox(_typeComboBox);
            _typeComboBox.DropDown += TypeComboBox_DropDown;
            layout.Controls.Add(_typeComboBox, 1, 0);

            AddNumberRow(layout, "시작 거리(mm)", _startDistanceBox, 1, 0, 100000000, 0);
            AddNumberRow(layout, "종료 거리(mm)", _endDistanceBox, 2, 0, 100000000, 0);
            AddNumberRow(layout, "구간 길이(mm)", _segmentLengthBox, 3, 100, 1000000, 3000);
            AddNumberRow(layout, "좌우 Offset(mm)", _offsetBox, 4, -1000000, 1000000, 0);
            AddNumberRow(layout, "설치 높이(mm)", _elevationBox, 5, -1000000, 1000000, 1200);
            AddNumberRow(layout, "트레이 폭(mm)", _widthBox, 6, 1, 1000000, 300);
            AddNumberRow(layout, "트레이 높이(mm)", _heightBox, 7, 1, 1000000, 100);

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.WrapContents = false;
            buttonPanel.Padding = new Padding(0, 12, 0, 0);
            buttonPanel.BackColor = WindowBackColor;
            root.Controls.Add(buttonPanel, 0, 2);

            var okButton = CreateButton("확인", PrimaryColor, DrawingColor.White);
            okButton.Click += OkButton_Click;

            var cancelButton = CreateButton("취소", DrawingColor.White, LabelColor);
            cancelButton.DialogResult = DialogResult.Cancel;

            var previewButton = CreateButton("미리보기", DrawingColor.FromArgb(224, 247, 250), PrimaryColor);
            previewButton.Click += PreviewButton_Click;

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(previewButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void LoadCableTrayTypes()
        {
            var types = new FilteredElementCollector(_document)
                .OfClass(typeof(CableTrayType))
                .Cast<CableTrayType>()
                .OrderBy(t => t.Name)
                .ToList();

            foreach (CableTrayType type in types)
                _typeComboBox.Items.Add(new CableTrayTypeListItem(type));

            UpdateDropDownWidth(_typeComboBox);

            if (_typeComboBox.Items.Count > 0)
                _typeComboBox.SelectedIndex = 0;
        }

        private void TypeComboBox_DropDown(object sender, EventArgs e)
        {
            UpdateDropDownWidth(_typeComboBox);
        }

        private void LoadSavedSettings()
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return;

            Dictionary<string, string> values = ReadSettingsFile(path);
            SetNumberValue(_startDistanceBox, GetDouble(values, "StartDistanceMm", (double)_startDistanceBox.Value));
            SetNumberValue(_endDistanceBox, GetDouble(values, "EndDistanceMm", (double)_endDistanceBox.Value));
            SetNumberValue(_segmentLengthBox, GetDouble(values, "SegmentLengthMm", (double)_segmentLengthBox.Value));
            SetNumberValue(_offsetBox, GetDouble(values, "OffsetMm", (double)_offsetBox.Value));
            SetNumberValue(_elevationBox, GetDouble(values, "ElevationMm", (double)_elevationBox.Value));
            SetNumberValue(_widthBox, GetDouble(values, "WidthMm", (double)_widthBox.Value));
            SetNumberValue(_heightBox, GetDouble(values, "HeightMm", (double)_heightBox.Value));

            string typeUniqueId;
            if (values.TryGetValue("CableTrayTypeUniqueId", out typeUniqueId))
                SelectCableTrayType(typeUniqueId);
        }

        private void HookPreviewInvalidation()
        {
            _typeComboBox.SelectedIndexChanged += InputChanged;
            _startDistanceBox.ValueChanged += InputChanged;
            _endDistanceBox.ValueChanged += InputChanged;
            _segmentLengthBox.ValueChanged += InputChanged;
            _offsetBox.ValueChanged += InputChanged;
            _elevationBox.ValueChanged += InputChanged;
            _widthBox.ValueChanged += InputChanged;
            _heightBox.ValueChanged += InputChanged;
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
                MessageBox.Show(this, ex.Message, "터널 케이블 트레이 자동배치");
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
            var selectedType = _typeComboBox.SelectedItem as CableTrayTypeListItem;
            if (selectedType == null)
            {
                MessageBox.Show(this, "케이블 트레이 타입을 선택하세요.", "터널 케이블 트레이 자동배치");
                return false;
            }

            if (_segmentLengthBox.Value <= 0)
            {
                MessageBox.Show(this, "구간 길이는 0보다 커야 합니다.", "터널 케이블 트레이 자동배치");
                return false;
            }

            Settings = new PlacementSettings
            {
                CableTrayTypeId = selectedType.Type.Id,
                StartDistanceMm = (double)_startDistanceBox.Value,
                EndDistanceMm = (double)_endDistanceBox.Value,
                SegmentLengthMm = (double)_segmentLengthBox.Value,
                OffsetMm = (double)_offsetBox.Value,
                ElevationMm = (double)_elevationBox.Value,
                WidthMm = (double)_widthBox.Value,
                HeightMm = (double)_heightBox.Value
            };

            SaveSettings(selectedType);
            return true;
        }

        private void SaveSettings(CableTrayTypeListItem selectedType)
        {
            var lines = new List<string>();
            lines.Add("CableTrayTypeUniqueId=" + selectedType.Type.UniqueId);
            lines.Add("StartDistanceMm=" + FormatDouble(Settings.StartDistanceMm));
            lines.Add("EndDistanceMm=" + FormatDouble(Settings.EndDistanceMm));
            lines.Add("SegmentLengthMm=" + FormatDouble(Settings.SegmentLengthMm));
            lines.Add("OffsetMm=" + FormatDouble(Settings.OffsetMm));
            lines.Add("ElevationMm=" + FormatDouble(Settings.ElevationMm));
            lines.Add("WidthMm=" + FormatDouble(Settings.WidthMm));
            lines.Add("HeightMm=" + FormatDouble(Settings.HeightMm));

            File.WriteAllLines(GetSettingsPath(), lines.ToArray());
        }

        private void SelectCableTrayType(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return;

            for (int i = 0; i < _typeComboBox.Items.Count; i++)
            {
                var item = _typeComboBox.Items[i] as CableTrayTypeListItem;
                if (item != null && item.Type.UniqueId == uniqueId)
                {
                    _typeComboBox.SelectedIndex = i;
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
                "TunnelCableTrayPlacementAddin");

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

                string key = line.Substring(0, separatorIndex);
                string value = line.Substring(separatorIndex + 1);
                result[key] = value;
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
            comboBox.Dock = DockStyle.Fill;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = DrawingColor.White;
            comboBox.ForeColor = LabelColor;
            comboBox.Margin = new Padding(0, 3, 0, 3);
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
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = DrawingColor.White;
            box.ForeColor = LabelColor;
            box.Margin = new Padding(0, 3, 0, 3);
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
