using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace TunnelLightingPlacementAddin
{
    public class TunnelLightingPlacementForm : System.Windows.Forms.Form
    {
        private readonly Document _document;
        private readonly ComboBox _symbolComboBox = new ComboBox();
        private readonly NumericUpDown _startDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _endDistanceBox = new NumericUpDown();
        private readonly NumericUpDown _spacingBox = new NumericUpDown();
        private readonly ComboBox _sideComboBox = new ComboBox();
        private readonly NumericUpDown _offsetBox = new NumericUpDown();
        private readonly NumericUpDown _heightBox = new NumericUpDown();
        private readonly NumericUpDown _rotationAngleBox = new NumericUpDown();
        private readonly TextBox _segmentNameBox = new TextBox();
        private readonly TextBox _stationParameterBox = new TextBox();
        private readonly TextBox _segmentParameterBox = new TextBox();
        private readonly TextBox _directionParameterBox = new TextBox();
        private readonly TextBox _offsetParameterBox = new TextBox();
        private readonly TextBox _heightParameterBox = new TextBox();

        public TunnelLightingPlacementForm(Document document)
        {
            _document = document;
            Settings = new PlacementSettings();
            InitializeComponent();
            LoadFamilySymbols();
        }

        public PlacementSettings Settings { get; private set; }

        private void InitializeComponent()
        {
            Text = "터널 전등 자동배치";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            MinimumSize = new Size(520, 560);
            Size = new Size(560, 620);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.RowCount = 16;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddLabel(layout, "등기구 패밀리 타입", 0);
            ConfigureComboBox(_symbolComboBox);
            layout.Controls.Add(_symbolComboBox, 1, 0);

            AddNumberRow(layout, "시작 거리(mm)", _startDistanceBox, 1, 0, 100000000, 0);
            AddNumberRow(layout, "종료 거리(mm)", _endDistanceBox, 2, 0, 100000000, 0);
            AddNumberRow(layout, "설치 간격(mm)", _spacingBox, 3, 1, 1000000, 500);

            AddLabel(layout, "설치 위치", 4);
            ConfigureComboBox(_sideComboBox);
            _sideComboBox.Items.Add("중앙");
            _sideComboBox.Items.Add("좌측");
            _sideComboBox.Items.Add("우측");
            _sideComboBox.SelectedIndex = 0;
            layout.Controls.Add(_sideComboBox, 1, 4);

            AddNumberRow(layout, "Offset(mm)", _offsetBox, 5, -1000000, 1000000, 0);
            AddNumberRow(layout, "높이(mm)", _heightBox, 6, -1000000, 1000000, 0);
            AddTextRow(layout, "구간명", _segmentNameBox, 7, "기본구간");
            AddTextRow(layout, "측점 파라미터명", _stationParameterBox, 8, "측점");
            AddTextRow(layout, "구간 파라미터명", _segmentParameterBox, 9, "구간");
            AddTextRow(layout, "방향 파라미터명", _directionParameterBox, 10, "방향");
            AddTextRow(layout, "Offset 파라미터명", _offsetParameterBox, 11, "중심선Offset");
            AddTextRow(layout, "높이 파라미터명", _heightParameterBox, 12, "설치높이");

            AddNumberRow(layout, "회전각도(도)", _rotationAngleBox, 13, -360, 360, 180);

            var note = new Label();
            note.Text = "확인을 누른 뒤 Revit 화면에서 터널 중심선 Model Line 또는 Curve 요소를 선택합니다.";
            note.Dock = DockStyle.Fill;
            note.ForeColor = System.Drawing.Color.FromArgb(75, 75, 75);
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.AutoSize = true;
            layout.Controls.Add(note, 0, 14);
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

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);
            layout.Controls.Add(buttonPanel, 0, 15);
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

        private void OkButton_Click(object sender, EventArgs e)
        {
            var selectedSymbol = _symbolComboBox.SelectedItem as FamilySymbolListItem;
            if (selectedSymbol == null)
            {
                MessageBox.Show(this, "등기구 패밀리 타입을 선택하세요.", "터널 전등 자동배치");
                return;
            }

            if (_spacingBox.Value <= 0)
            {
                MessageBox.Show(this, "설치 간격은 0보다 커야 합니다.", "터널 전등 자동배치");
                return;
            }

            Settings = new PlacementSettings
            {
                FamilySymbolId = selectedSymbol.Symbol.Id,
                StartDistanceMm = (double)_startDistanceBox.Value,
                EndDistanceMm = (double)_endDistanceBox.Value,
                SpacingMm = (double)_spacingBox.Value,
                Side = GetSelectedSide(),
                OffsetMm = (double)_offsetBox.Value,
                HeightMm = (double)_heightBox.Value,
                RotationAngleDegrees = (double)_rotationAngleBox.Value,
                SegmentName = _segmentNameBox.Text,
                StationParameterName = _stationParameterBox.Text,
                SegmentParameterName = _segmentParameterBox.Text,
                DirectionParameterName = _directionParameterBox.Text,
                OffsetParameterName = _offsetParameterBox.Text,
                HeightParameterName = _heightParameterBox.Text
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private PlacementSide GetSelectedSide()
        {
            if (_sideComboBox.SelectedIndex == 1)
                return PlacementSide.Left;

            if (_sideComboBox.SelectedIndex == 2)
                return PlacementSide.Right;

            return PlacementSide.Center;
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

        private static void AddTextRow(TableLayoutPanel layout, string label, TextBox box, int row, string value)
        {
            AddLabel(layout, label, row);
            box.Dock = DockStyle.Fill;
            box.Text = value;
            layout.Controls.Add(box, 1, row);
        }
    }
}
