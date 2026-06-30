using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using DrawingColor = System.Drawing.Color;
using FormsPanel = System.Windows.Forms.Panel;

namespace CableGeneratorAddin
{
    public class CableGeneratorForm : System.Windows.Forms.Form
    {
        private static readonly DrawingColor WindowBackColor = DrawingColor.FromArgb(246, 247, 249);
        private static readonly DrawingColor PanelBackColor = DrawingColor.White;
        private static readonly DrawingColor LabelColor = DrawingColor.FromArgb(31, 41, 55);
        private static readonly DrawingColor BorderColor = DrawingColor.FromArgb(209, 213, 219);
        private static readonly DrawingColor PrimaryColor = DrawingColor.FromArgb(15, 118, 110);

        private readonly Document _document;
        private readonly ComboBox _conduitTypeBox = new ComboBox();
        private readonly NumericUpDown _cableCountBox = new NumericUpDown();
        private readonly NumericUpDown _diameterBox = new NumericUpDown();
        private readonly NumericUpDown _gapBox = new NumericUpDown();
        private readonly NumericUpDown _offsetBox = new NumericUpDown();

        public CableGeneratorForm(Document document)
        {
            _document = document;
            Settings = new CableGeneratorSettings();
            InitializeComponent();
            LoadConduitTypes();
        }

        public CableGeneratorSettings Settings { get; private set; }

        private void InitializeComponent()
        {
            Text = "Cables Generator";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            BackColor = WindowBackColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 320);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(14);
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            Controls.Add(root);

            var title = new Label();
            title.Text = "Layout Settings:";
            title.Dock = DockStyle.Fill;
            title.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.ForeColor = LabelColor;
            root.Controls.Add(title, 0, 0);
            root.SetColumnSpan(title, 2);

            var previewPanel = new PreviewPanel();
            previewPanel.Dock = DockStyle.Fill;
            previewPanel.BackColor = PanelBackColor;
            previewPanel.Margin = new Padding(0, 0, 14, 0);
            root.Controls.Add(previewPanel, 0, 1);

            var fieldsPanel = new FormsPanel();
            fieldsPanel.Dock = DockStyle.Fill;
            fieldsPanel.BackColor = PanelBackColor;
            fieldsPanel.Padding = new Padding(14, 10, 6, 10);
            root.Controls.Add(fieldsPanel, 1, 1);

            var fields = new TableLayoutPanel();
            fields.Dock = DockStyle.Fill;
            fields.ColumnCount = 3;
            fields.RowCount = 5;
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++)
                fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            fieldsPanel.Controls.Add(fields);

            AddLabel(fields, "Conduit Type", 0);
            ConfigureComboBox(_conduitTypeBox);
            fields.Controls.Add(_conduitTypeBox, 1, 0);
            fields.SetColumnSpan(_conduitTypeBox, 2);

            AddNumberRow(fields, "Number of Cables", _cableCountBox, 1, 1, 100, 4, "Cable");
            AddNumberRow(fields, "Diameter of Cables", _diameterBox, 2, 1, 1000, 10, "mm");
            AddNumberRow(fields, "Gap Between Cables (G)", _gapBox, 3, 0, 1000, 1, "mm");
            AddNumberRow(fields, "Offset from Cable tray (O)", _offsetBox, 4, 0, 1000, 1, "mm");

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.WrapContents = false;
            buttonPanel.Padding = new Padding(0, 12, 0, 0);
            buttonPanel.BackColor = WindowBackColor;
            root.Controls.Add(buttonPanel, 0, 2);
            root.SetColumnSpan(buttonPanel, 2);

            var cancelButton = CreateButton("Cancel", DrawingColor.White, LabelColor);
            cancelButton.DialogResult = DialogResult.Cancel;

            var createButton = CreateButton("Create", PrimaryColor, DrawingColor.White);
            createButton.Click += CreateButton_Click;

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(createButton);

            AcceptButton = createButton;
            CancelButton = cancelButton;
        }

        private void LoadConduitTypes()
        {
            var types = new FilteredElementCollector(_document)
                .OfClass(typeof(ConduitType))
                .Cast<ConduitType>()
                .OrderBy(t => t.Name)
                .ToList();

            foreach (ConduitType type in types)
                _conduitTypeBox.Items.Add(new ConduitTypeListItem(type));

            if (_conduitTypeBox.Items.Count > 0)
                _conduitTypeBox.SelectedIndex = 0;
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            var selectedType = _conduitTypeBox.SelectedItem as ConduitTypeListItem;
            if (selectedType == null)
            {
                MessageBox.Show(this, "Select a conduit type first.", "Cable Generator");
                return;
            }

            Settings = new CableGeneratorSettings
            {
                ConduitTypeId = selectedType.Type.Id,
                CableCount = (int)_cableCountBox.Value,
                CableDiameterMm = (double)_diameterBox.Value,
                GapMm = (double)_gapBox.Value,
                TrayOffsetMm = (double)_offsetBox.Value
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private static void ConfigureComboBox(ComboBox comboBox)
        {
            comboBox.Dock = DockStyle.Fill;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = DrawingColor.White;
            comboBox.ForeColor = LabelColor;
            comboBox.Margin = new Padding(0, 4, 0, 4);
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int row)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = LabelColor;
            layout.Controls.Add(label, 0, row);
        }

        private static void AddNumberRow(
            TableLayoutPanel layout,
            string labelText,
            NumericUpDown box,
            int row,
            decimal minimum,
            decimal maximum,
            decimal value,
            string unit)
        {
            AddLabel(layout, labelText, row);
            box.Dock = DockStyle.Fill;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.DecimalPlaces = 0;
            box.Increment = 1;
            box.Value = value;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = DrawingColor.White;
            box.ForeColor = LabelColor;
            box.Margin = new Padding(0, 4, 8, 4);
            layout.Controls.Add(box, 1, row);

            var unitLabel = new Label();
            unitLabel.Text = unit;
            unitLabel.Dock = DockStyle.Fill;
            unitLabel.TextAlign = ContentAlignment.MiddleLeft;
            unitLabel.ForeColor = LabelColor;
            unitLabel.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            layout.Controls.Add(unitLabel, 2, row);
        }

        private static Button CreateButton(string text, DrawingColor backColor, DrawingColor foreColor)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 132;
            button.Height = 34;
            button.Margin = new Padding(12, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private class PreviewPanel : FormsPanel
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (var labelBrush = new SolidBrush(DrawingColor.FromArgb(75, 85, 99)))
                using (var trayPen = new Pen(DrawingColor.FromArgb(31, 41, 55), 2F))
                using (var arrowPen = new Pen(DrawingColor.FromArgb(31, 41, 55), 2F))
                using (var cableBrush = new SolidBrush(DrawingColor.FromArgb(220, 38, 38)))
                using (var font = new Font("Malgun Gothic", 8F))
                {
                    arrowPen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                    System.Drawing.Rectangle tray = new System.Drawing.Rectangle(42, 82, 150, 76);
                    g.DrawLine(trayPen, tray.Left, tray.Top, tray.Left, tray.Bottom);
                    g.DrawLine(trayPen, tray.Left, tray.Bottom, tray.Right, tray.Bottom);
                    g.DrawLine(trayPen, tray.Right, tray.Top, tray.Right, tray.Bottom);

                    g.DrawString("Cable Tray", font, labelBrush, 4, 58);
                    g.DrawLine(arrowPen, 34, 70, 64, 96);

                    g.DrawString("Cable", font, labelBrush, 96, 40);
                    g.DrawLine(arrowPen, 111, 52, 111, 104);

                    for (int i = 0; i < 5; i++)
                        g.FillEllipse(cableBrush, 86 + i * 22, 118, 13, 13);

                    g.DrawString("(G)", font, labelBrush, 142, 105);
                    g.DrawString("(O)", font, labelBrush, 82, 138);
                }
            }
        }
    }
}
