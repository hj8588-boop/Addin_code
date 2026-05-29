using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace LightingCalculationAddin
{
    public class LightingCalculationForm : System.Windows.Forms.Form
    {
        private readonly Document document;
        private readonly IList<LightingSpaceRow> allRows;
        private readonly BindingList<LightingSpaceRow> rows;
        private readonly IList<LightingFixtureType> fixtureTypes;
        private readonly DataGridView grid;
        private readonly CheckedListBox fixtureList;
        private readonly Label titleLabel;
        private readonly SplitContainer split;
        private readonly System.Windows.Forms.TextBox exportPathTextBox;
        private const int GroupHeaderHeight = 18;
        private const int HeaderFilterGlyphWidth = 14;
        private System.Windows.Forms.TextBox fluxInputTextBox;
        private bool updatingFixtureList;
        private bool initializingSelection;
        private int lastSelectionCheckRowIndex = -1;
        private int savedSplitterDistance = -1;
        private readonly List<int> selectionRowsBeforeCheckboxClick = new List<int>();
        private readonly List<int> editRowsBeforeCellEdit = new List<int>();
        private readonly Dictionary<string, HashSet<string>> activeFilters = new Dictionary<string, HashSet<string>>();
        private readonly string settingsFilePath;

        public LightingCalculationForm(Document document)
        {
            this.document = document;
            fixtureTypes = LightingCalculationService.LoadFixtureTypes(document);
            allRows = new List<LightingSpaceRow>(LightingCalculationService.LoadRows(document, fixtureTypes));
            rows = new BindingList<LightingSpaceRow>(new List<LightingSpaceRow>(allRows));
            settingsFilePath = GetSettingsFilePath(document);

            Text = "Lighting Calculation";
            Width = 1280;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9F);
            MinimumSize = new Size(1100, 650);

            var mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 4;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(mainLayout);

            titleLabel = new Label();
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Height = 48;
            titleLabel.Padding = new Padding(12, 12, 12, 8);
            titleLabel.Font = new Font("Malgun Gothic", 12F, FontStyle.Bold);
            titleLabel.Text = string.Empty;
            titleLabel.AutoEllipsis = true;
            mainLayout.Controls.Add(titleLabel, 0, 0);

            var bottomPanel = new FlowLayoutPanel();
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.Height = 48;
            bottomPanel.FlowDirection = FlowDirection.RightToLeft;
            bottomPanel.Padding = new Padding(8);
            mainLayout.Controls.Add(bottomPanel, 0, 3);

            var closeButton = CreateTextButton("Close", 96);
            closeButton.Click += delegate { Close(); };
            bottomPanel.Controls.Add(closeButton);

            var saveButton = CreateTextButton("Save", 96);
            saveButton.Click += SaveButtonClick;
            bottomPanel.Controls.Add(saveButton);

            var exportButton = CreateTextButton("Export Excel", 112);
            exportButton.Click += ExportButtonClick;
            bottomPanel.Controls.Add(exportButton);

            var recalcButton = CreateTextButton("Recalculate", 112);
            recalcButton.Click += delegate { RecalculateAll(); };
            bottomPanel.Controls.Add(recalcButton);

            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.SplitterWidth = 6;
            split.Panel1.Padding = new Padding(10, 0, 6, 8);
            split.Panel2.Padding = new Padding(6, 0, 10, 8);
            mainLayout.Controls.Add(split, 0, 1);

            var exportPathPanel = new TableLayoutPanel();
            exportPathPanel.Dock = DockStyle.Fill;
            exportPathPanel.ColumnCount = 3;
            exportPathPanel.RowCount = 1;
            exportPathPanel.Padding = new Padding(10, 4, 10, 4);
            exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            exportPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            mainLayout.Controls.Add(exportPathPanel, 0, 2);

            var exportPathLabel = CreateTextLabel("Export Path", ContentAlignment.MiddleLeft);
            exportPathLabel.Dock = DockStyle.None;
            exportPathLabel.Anchor = AnchorStyles.None;
            exportPathLabel.AutoSize = true;
            exportPathPanel.Controls.Add(exportPathLabel, 0, 0);

            exportPathTextBox = new System.Windows.Forms.TextBox();
            exportPathTextBox.Dock = DockStyle.None;
            exportPathTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            exportPathTextBox.Text = GetDefaultExportDirectory();
            exportPathPanel.Controls.Add(exportPathTextBox, 1, 0);

            var browseExportPathButton = CreateTextButton("Browse", 110);
            browseExportPathButton.AutoSize = false;
            browseExportPathButton.Size = new Size(110, 28);
            browseExportPathButton.Anchor = AnchorStyles.None;
            browseExportPathButton.Click += BrowseExportPathButtonClick;
            exportPathPanel.Controls.Add(browseExportPathButton, 2, 0);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersWidth = 28;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ColumnHeadersVisible = true;
            grid.ColumnHeadersHeight = 52;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(230, 236, 242);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.Black;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.TopLeftHeaderCell.Value = "No";
            grid.CellBeginEdit += GridCellBeginEdit;
            grid.CellEndEdit += GridCellEndEdit;
            grid.CellMouseDown += GridCellMouseDown;
            grid.CellContentClick += GridCellContentClick;
            grid.CurrentCellDirtyStateChanged += GridCurrentCellDirtyStateChanged;
            grid.SelectionChanged += GridSelectionChanged;
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.CellPainting += GridCellPainting;
            grid.Paint += GridPaint;
            grid.Scroll += delegate(object sender, ScrollEventArgs args) { grid.Invalidate(); };
            grid.ColumnWidthChanged += delegate(object sender, DataGridViewColumnEventArgs args) { grid.Invalidate(); };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs args) { args.ThrowException = false; };

            split.Panel1.Controls.Add(grid);

            AddSelectionColumn();
            AddNumberColumn("ElementIdValue", "ElementId", 82, true);
            AddTextColumn("LevelName", "층별", 120, true);
            AddTextColumn("SpaceName", "Space", 180, true);
            AddNumberColumn("AreaM2", "면적 m2", 76, true);
            AddNumberColumn("LengthM", "가로 m", 70, true);
            AddNumberColumn("WidthM", "세로 m", 70, true);
            AddNumberColumn("EffectiveHeightM", "광원고", 72, false);
            AddNumberColumn("RoomIndex", "실지수", 70, true);
            AddTextColumn("FixtureType", "조명 타입", 210, false);
            AddNumberColumn("FixtureFluxLm", "광속 lm", 90, false);
            AddNumberColumn("CeilingReflectance", "천정", 86, false);
            AddNumberColumn("WallReflectance", "벽", 76, false);
            AddNumberColumn("FloorReflectance", "바닥", 86, false);
            AddNumberColumn("UtilizationFactor", "조명율", 70, true);
            AddNumberColumn("MaintenanceFactor", "보수율", 72, false);
            AddNumberColumn("RequiredLux", "요구조도", 82, false);
            AddNumberColumn("RawRequiredCount", "계산등수", 82, true);
            AddNumberColumn("RequiredCount", "필요등수", 76, true);
            AddNumberColumn("CalculatedIlluminance", "계산조도", 82, true);
            AddTextColumn("Status", "상태", 76, true);
            AddTextColumn("Message", "메시지", 220, true);

            grid.DataSource = rows;

            var rightPanel = new TableLayoutPanel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.ColumnCount = 1;
            rightPanel.RowCount = 3;
            rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            split.Panel2.Controls.Add(rightPanel);

            var fixtureLabel = CreateTextLabel("Selected Space Fixture Type", ContentAlignment.MiddleLeft);
            fixtureLabel.Height = 38;
            fixtureLabel.Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
            rightPanel.Controls.Add(fixtureLabel, 0, 0);

            fixtureList = new CheckedListBox();
            fixtureList.Dock = DockStyle.Fill;
            fixtureList.CheckOnClick = true;
            fixtureList.ItemCheck += FixtureListItemCheck;
            fixtureList.SelectedIndexChanged += FixtureListSelectedIndexChanged;
            rightPanel.Controls.Add(fixtureList, 0, 1);

            foreach (LightingFixtureType fixture in fixtureTypes)
            {
                string fluxText = fixture.FluxLm > 0 ? string.Format(CultureInfo.InvariantCulture, " ({0:0.##} lm)", fixture.FluxLm) : " (광속 없음)";
                fixtureList.Items.Add(fixture.Label + fluxText);
            }

            var fluxWritePanel = new TableLayoutPanel();
            fluxWritePanel.Dock = DockStyle.Fill;
            fluxWritePanel.ColumnCount = 3;
            fluxWritePanel.RowCount = 1;
            fluxWritePanel.Padding = new Padding(0, 3, 0, 3);
            fluxWritePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            fluxWritePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            fluxWritePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
            rightPanel.Controls.Add(fluxWritePanel, 0, 2);

            var fluxWriteLabel = CreateTextLabel("Flux (lm)", ContentAlignment.MiddleRight);
            fluxWriteLabel.Dock = DockStyle.None;
            fluxWriteLabel.Anchor = AnchorStyles.None;
            fluxWriteLabel.AutoSize = true;
            fluxWritePanel.Controls.Add(fluxWriteLabel, 0, 0);

            fluxInputTextBox = new System.Windows.Forms.TextBox();
            fluxInputTextBox.Dock = DockStyle.None;
            fluxInputTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            fluxWritePanel.Controls.Add(fluxInputTextBox, 1, 0);

            var writeFluxButton = CreateTextButton("Apply Input", 104);
            writeFluxButton.AutoSize = false;
            writeFluxButton.Size = new Size(104, 28);
            writeFluxButton.Anchor = AnchorStyles.None;
            writeFluxButton.Click += WriteFluxButtonClick;
            fluxWritePanel.Controls.Add(writeFluxButton, 2, 0);

            UpdateTitle();
            LoadFormSettings();
            Shown += FormShown;
        }

        private Button CreateTextButton(string text, int minimumWidth)
        {
            var button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowOnly;
            button.MinimumSize = new Size(GetTextButtonWidth(text, minimumWidth), 28);
            button.Padding = new Padding(8, 0, 8, 0);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.UseVisualStyleBackColor = true;
            return button;
        }

        private Label CreateTextLabel(string text, ContentAlignment textAlign)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = textAlign;
            label.AutoEllipsis = true;
            label.MinimumSize = new Size(GetTextControlWidth(text, 0), 0);
            return label;
        }

        private int GetTextButtonWidth(string text, int minimumWidth)
        {
            return Math.Max(minimumWidth, GetTextControlWidth(text, 28));
        }

        private int GetTextControlWidth(string text, int horizontalPadding)
        {
            Size textSize = TextRenderer.MeasureText(text, Font);
            return textSize.Width + horizontalPadding;
        }

        private void FormShown(object sender, EventArgs e)
        {
            int desiredRightPanelWidth = 320;
            int availableWidth = Math.Max(0, split.ClientSize.Width - split.SplitterWidth);
            if (savedSplitterDistance > 0 && savedSplitterDistance < availableWidth - split.Panel2MinSize)
            {
                split.SplitterDistance = savedSplitterDistance;
            }
            else if (availableWidth > desiredRightPanelWidth + split.Panel1MinSize)
            {
                split.SplitterDistance = availableWidth - desiredRightPanelWidth;
                split.Panel2MinSize = Math.Min(280, split.Panel2.Width);
            }
            grid.ColumnHeadersVisible = true;
            InitializeGridSelection();
        }

        private void InitializeGridSelection()
        {
            if (IsDisposed)
            {
                return;
            }

            initializingSelection = true;
            if (grid.Rows.Count > 0 && grid.Columns.Count > 0)
            {
                grid.ClearSelection();
                grid.CurrentCell = grid.Rows[0].Cells[0];
                grid.Rows[0].Selected = true;
            }
            initializingSelection = false;

            SyncFixtureChecks();
        }

        private void AddTextColumn(string property, string header, int width, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn();
            column.DataPropertyName = property;
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = readOnly;
            grid.Columns.Add(column);
        }

        private void AddSelectionColumn()
        {
            var column = new DataGridViewCheckBoxColumn();
            column.DataPropertyName = "IsSelected";
            column.HeaderText = "선택";
            column.Width = 52;
            column.ReadOnly = false;
            grid.Columns.Add(column);
        }

        private void AddNumberColumn(string property, string header, int width, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn();
            column.DataPropertyName = property;
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = readOnly;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Format = property == "RequiredCount" ? "0" : "0.####";
            grid.Columns.Add(column);
        }

        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            bool isGroupedHeaderColumn = IsRoomConditionColumn(column.DataPropertyName) ||
                IsFixtureSpecColumn(column.DataPropertyName) ||
                IsReflectanceColumn(column.DataPropertyName);
            System.Drawing.Rectangle bounds = e.CellBounds;
            System.Drawing.Rectangle textBounds = isGroupedHeaderColumn
                ? new System.Drawing.Rectangle(bounds.Left, bounds.Top + GroupHeaderHeight + 1, bounds.Width, bounds.Height - GroupHeaderHeight - 1)
                : bounds;

            using (var background = new SolidBrush(grid.ColumnHeadersDefaultCellStyle.BackColor))
            using (var border = new Pen(grid.GridColor))
            using (var text = new SolidBrush(grid.ColumnHeadersDefaultCellStyle.ForeColor))
            {
                e.Graphics.FillRectangle(background, bounds);
                e.Graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

                TextRenderer.DrawText(
                    e.Graphics,
                    column.HeaderText,
                    grid.ColumnHeadersDefaultCellStyle.Font,
                    GetHeaderTextBounds(textBounds),
                    grid.ColumnHeadersDefaultCellStyle.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                DrawHeaderFilterGlyph(e.Graphics, column, textBounds);
            }

            e.Handled = true;
        }

        private System.Drawing.Rectangle GetHeaderTextBounds(System.Drawing.Rectangle bounds)
        {
            if (bounds.Width <= HeaderFilterGlyphWidth + 4)
            {
                return bounds;
            }

            return new System.Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Width - HeaderFilterGlyphWidth, bounds.Height);
        }

        private void DrawHeaderFilterGlyph(Graphics graphics, DataGridViewColumn column, System.Drawing.Rectangle bounds)
        {
            if (string.IsNullOrWhiteSpace(column.DataPropertyName) ||
                string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            bool isFiltered = activeFilters.ContainsKey(column.DataPropertyName);
            int centerX = bounds.Right - 8;
            int centerY = bounds.Top + bounds.Height / 2;
            System.Drawing.Point[] arrow = new[]
            {
                new System.Drawing.Point(centerX - 4, centerY - 2),
                new System.Drawing.Point(centerX + 4, centerY - 2),
                new System.Drawing.Point(centerX, centerY + 3)
            };

            using (var brush = new SolidBrush(isFiltered ? System.Drawing.Color.FromArgb(20, 90, 160) : System.Drawing.Color.DimGray))
            {
                graphics.FillPolygon(brush, arrow);
            }
        }

        private void GridPaint(object sender, PaintEventArgs e)
        {
            DrawColumnGroupHeader(e.Graphics, "AreaM2", "EffectiveHeightM", "실 조건");
            DrawColumnGroupHeader(e.Graphics, "FixtureType", "FixtureFluxLm", "조명 사양");
            DrawColumnGroupHeader(e.Graphics, "CeilingReflectance", "FloorReflectance", "반사율(%)");
            
        }

        private void DrawColumnGroupHeader(Graphics graphics, string firstPropertyName, string lastPropertyName, string title)
        {
            DataGridViewColumn firstColumn = FindGridColumn(firstPropertyName);
            DataGridViewColumn lastColumn = FindGridColumn(lastPropertyName);
            if (firstColumn == null || lastColumn == null)
            {
                return;
            }

            System.Drawing.Rectangle first = grid.GetCellDisplayRectangle(firstColumn.Index, -1, true);
            System.Drawing.Rectangle last = grid.GetCellDisplayRectangle(lastColumn.Index, -1, true);
            if (first.Width <= 0 && last.Width <= 0)
            {
                return;
            }

            int left = first.Width > 0 ? first.Left : last.Left;
            int right = last.Width > 0 ? last.Right : first.Right;
            System.Drawing.Rectangle groupBounds = new System.Drawing.Rectangle(left, first.Top, right - left, GroupHeaderHeight);
            if (groupBounds.Width <= 0)
            {
                return;
            }

            using (var background = new SolidBrush(grid.ColumnHeadersDefaultCellStyle.BackColor))
            using (var border = new Pen(grid.GridColor))
            {
                graphics.FillRectangle(background, groupBounds);
                graphics.DrawRectangle(border, groupBounds.Left, groupBounds.Top, groupBounds.Width - 1, groupBounds.Height - 1);
                TextRenderer.DrawText(
                    graphics,
                    title,
                    grid.ColumnHeadersDefaultCellStyle.Font,
                    groupBounds,
                    grid.ColumnHeadersDefaultCellStyle.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private DataGridViewColumn FindGridColumn(string propertyName)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (string.Equals(column.DataPropertyName, propertyName, StringComparison.Ordinal))
                {
                    return column;
                }
            }

            return null;
        }

        private static bool IsRoomConditionColumn(string propertyName)
        {
            return string.Equals(propertyName, "AreaM2", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "LengthM", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "EffectiveHeightM", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "WidthM", StringComparison.Ordinal);
        }

        private static bool IsFixtureSpecColumn(string propertyName)
        {
            return string.Equals(propertyName, "FixtureType", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "FixtureFluxLm", StringComparison.Ordinal);
        }

        private static bool IsReflectanceColumn(string propertyName)
        {
            return string.Equals(propertyName, "CeilingReflectance", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "WallReflectance", StringComparison.Ordinal) ||
                   string.Equals(propertyName, "FloorReflectance", StringComparison.Ordinal);
        }

        private void GridCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            CaptureRowsBeforeCellEdit(e.RowIndex, e.ColumnIndex, false);
        }

        private void GridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                editRowsBeforeCellEdit.Clear();
                return;
            }

            LightingSpaceRow row = grid.Rows[e.RowIndex].DataBoundItem as LightingSpaceRow;
            if (row == null)
            {
                editRowsBeforeCellEdit.Clear();
                return;
            }

            LightingCalculationService.ApplyFixtureDefaults(row, fixtureTypes);
            LightingCalculationService.Recalculate(row);
            RefreshRowDisplay(row);
            ApplyEditedCellValueToSelectedRows(row, grid.Columns[e.ColumnIndex]);
            SyncFixtureChecks();
            editRowsBeforeCellEdit.Clear();
        }

        private void CaptureRowsBeforeCellEdit(int rowIndex, int columnIndex, bool overwriteExisting)
        {
            if (overwriteExisting)
            {
                editRowsBeforeCellEdit.Clear();
            }
            else if (editRowsBeforeCellEdit.Count > 0)
            {
                return;
            }

            if (rowIndex < 0 || columnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[columnIndex];
            if (column == null ||
                column.ReadOnly ||
                string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            DataGridViewRow clickedGridRow = grid.Rows[rowIndex];
            if (!clickedGridRow.Selected)
            {
                return;
            }

            foreach (DataGridViewRow selectedRow in grid.SelectedRows)
            {
                editRowsBeforeCellEdit.Add(selectedRow.Index);
            }

            if (editRowsBeforeCellEdit.Count <= 1)
            {
                editRowsBeforeCellEdit.Clear();
            }
        }

        private void ApplyEditedCellValueToSelectedRows(LightingSpaceRow sourceRow, DataGridViewColumn column)
        {
            if (column == null ||
                column.ReadOnly ||
                string.IsNullOrWhiteSpace(column.DataPropertyName) ||
                string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            PropertyDescriptor property = TypeDescriptor.GetProperties(typeof(LightingSpaceRow))[column.DataPropertyName];
            if (property == null || property.IsReadOnly)
            {
                return;
            }

            object sourceValue = property.GetValue(sourceRow);
            IList<LightingSpaceRow> targetRows = GetBulkEditTargetRows(sourceRow);
            foreach (LightingSpaceRow targetRow in targetRows)
            {
                if (targetRow == null || ReferenceEquals(targetRow, sourceRow))
                {
                    continue;
                }

                property.SetValue(targetRow, sourceValue);
                LightingCalculationService.ApplyFixtureDefaults(targetRow, fixtureTypes);
                LightingCalculationService.Recalculate(targetRow);
                RefreshRowDisplay(targetRow);
            }
        }

        private IList<LightingSpaceRow> GetBulkEditTargetRows(LightingSpaceRow sourceRow)
        {
            IList<LightingSpaceRow> checkedRows = GetCheckedRows();
            if (checkedRows.Count > 1 && checkedRows.Contains(sourceRow))
            {
                return checkedRows;
            }

            IList<LightingSpaceRow> editRows = GetRowsFromSnapshot(editRowsBeforeCellEdit);
            if (editRows.Count > 1 && editRows.Contains(sourceRow))
            {
                return editRows;
            }

            IList<LightingSpaceRow> selectedRows = GetGridSelectedRows();
            if (selectedRows.Count > 1 && selectedRows.Contains(sourceRow))
            {
                return selectedRows;
            }

            return new List<LightingSpaceRow>();
        }

        private void GridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void GridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            selectionRowsBeforeCheckboxClick.Clear();
            CaptureRowsBeforeCellEdit(e.RowIndex, e.ColumnIndex, true);

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (!string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            foreach (DataGridViewRow selectedRow in grid.SelectedRows)
            {
                selectionRowsBeforeCheckboxClick.Add(selectedRow.Index);
            }
        }

        private void GridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (!string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            LightingSpaceRow clickedRow = grid.Rows[e.RowIndex].DataBoundItem as LightingSpaceRow;
            if (clickedRow == null)
            {
                return;
            }

            bool shiftPressed = (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            if (shiftPressed && lastSelectionCheckRowIndex >= 0)
            {
                int start = Math.Min(lastSelectionCheckRowIndex, e.RowIndex);
                int end = Math.Max(lastSelectionCheckRowIndex, e.RowIndex);
                bool targetValue = clickedRow.IsSelected;

                for (int rowIndex = start; rowIndex <= end; rowIndex++)
                {
                    LightingSpaceRow row = grid.Rows[rowIndex].DataBoundItem as LightingSpaceRow;
                    if (row != null)
                    {
                        row.IsSelected = targetValue;
                        RefreshRowDisplay(row);
                    }
                }
            }
            else
            {
                IList<LightingSpaceRow> selectedRows = GetRowsFromSnapshot(selectionRowsBeforeCheckboxClick);
                if (selectedRows.Count == 0)
                {
                    selectedRows = GetGridSelectedRows();
                }
                if (selectedRows.Count > 1)
                {
                    bool targetValue = clickedRow.IsSelected;
                    foreach (LightingSpaceRow row in selectedRows)
                    {
                        row.IsSelected = targetValue;
                        RefreshRowDisplay(row);
                    }
                }
            }

            lastSelectionCheckRowIndex = e.RowIndex;
        }

        private void GridSelectionChanged(object sender, EventArgs e)
        {
            if (initializingSelection)
            {
                return;
            }

            SyncFixtureChecks();
        }

        private void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (string.IsNullOrWhiteSpace(column.DataPropertyName) ||
                string.Equals(column.DataPropertyName, "IsSelected", StringComparison.Ordinal))
            {
                return;
            }

            ContextMenuStrip menu = BuildFilterMenu(column);

            System.Drawing.Rectangle headerCell = grid.GetCellDisplayRectangle(e.ColumnIndex, -1, true);
            menu.Show(grid, new System.Drawing.Point(headerCell.Left, headerCell.Bottom));
        }

        private ContextMenuStrip BuildFilterMenu(DataGridViewColumn column)
        {
            var menu = new ContextMenuStrip();

            IList<string> distinctValues = GetDistinctFilterValues(column.DataPropertyName);
            HashSet<string> selectedValues;
            bool hasColumnFilter = activeFilters.TryGetValue(column.DataPropertyName, out selectedValues);

            var panel = new TableLayoutPanel();
            panel.AutoSize = true;
            panel.ColumnCount = 1;
            panel.RowCount = 4;
            panel.Padding = new Padding(6);
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            var searchTextBox = new System.Windows.Forms.TextBox();
            searchTextBox.Dock = DockStyle.Fill;
            searchTextBox.Text = "Search";
            searchTextBox.ForeColor = System.Drawing.Color.Gray;
            panel.Controls.Add(searchTextBox, 0, 0);

            var selectAllCheckBox = new CheckBox();
            selectAllCheckBox.Dock = DockStyle.Fill;
            selectAllCheckBox.Text = "(Select All)";
            panel.Controls.Add(selectAllCheckBox, 0, 1);

            var valueList = new CheckedListBox();
            valueList.CheckOnClick = true;
            valueList.BorderStyle = BorderStyle.FixedSingle;
            valueList.Width = Math.Max(180, column.Width);
            valueList.Height = Math.Min(240, Math.Max(72, distinctValues.Count * 20 + 8));
            panel.Controls.Add(valueList, 0, 2);

            var filterSelectionStates = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string value in distinctValues)
            {
                filterSelectionStates[value] = !hasColumnFilter || selectedValues.Contains(value);
            }

            RefreshFilterValueList(valueList, distinctValues, filterSelectionStates, string.Empty);
            SyncSelectAllCheckBox(selectAllCheckBox, valueList);

            searchTextBox.GotFocus += delegate
            {
                if (string.Equals(searchTextBox.Text, "Search", StringComparison.Ordinal))
                {
                    searchTextBox.Text = string.Empty;
                    searchTextBox.ForeColor = System.Drawing.Color.Black;
                }
            };
            searchTextBox.LostFocus += delegate
            {
                if (string.IsNullOrWhiteSpace(searchTextBox.Text))
                {
                    searchTextBox.Text = "Search";
                    searchTextBox.ForeColor = System.Drawing.Color.Gray;
                }
            };
            searchTextBox.TextChanged += delegate
            {
                string keyword = string.Equals(searchTextBox.Text, "Search", StringComparison.Ordinal)
                    ? string.Empty
                    : searchTextBox.Text;
                RefreshFilterValueList(valueList, distinctValues, filterSelectionStates, keyword);
                SyncSelectAllCheckBox(selectAllCheckBox, valueList);
            };

            selectAllCheckBox.CheckedChanged += delegate
            {
                SetVisibleFilterItemsChecked(valueList, filterSelectionStates, selectAllCheckBox.Checked);
            };
            valueList.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                FilterValueItem item = valueList.Items[e.Index] as FilterValueItem;
                if (item != null)
                {
                    filterSelectionStates[item.Value] = e.NewValue == CheckState.Checked;
                }

                BeginInvoke(new Action(delegate
                {
                    SyncSelectAllCheckBox(selectAllCheckBox, valueList);
                }));
            };

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Padding = new Padding(0, 4, 0, 4);
            buttonPanel.WrapContents = false;
            panel.Controls.Add(buttonPanel, 0, 3);

            var applyButton = CreateTextButton("Apply", 64);
            applyButton.AutoSize = false;
            applyButton.Size = new Size(64, 28);
            applyButton.Click += delegate
            {
                ApplyColumnFilter(column.DataPropertyName, filterSelectionStates, distinctValues);
                menu.Close();
            };
            buttonPanel.Controls.Add(applyButton);

            var clearAllButton = CreateTextButton("Clear Filter", 96);
            clearAllButton.AutoSize = false;
            clearAllButton.Size = new Size(96, 28);
            clearAllButton.Enabled = activeFilters.Count > 0;
            clearAllButton.Click += delegate
            {
                activeFilters.Clear();
                ApplyFilters();
                menu.Close();
            };
            buttonPanel.Controls.Add(clearAllButton);

            menu.Items.Add(new ToolStripControlHost(panel));
            return menu;
        }

        private static void RefreshFilterValueList(
            CheckedListBox valueList,
            IList<string> distinctValues,
            IDictionary<string, bool> filterSelectionStates,
            string keyword)
        {
            valueList.Items.Clear();
            foreach (string value in distinctValues)
            {
                string displayText = string.IsNullOrWhiteSpace(value) ? "(빈 값)" : value;
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    displayText.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }

                int itemIndex = valueList.Items.Add(new FilterValueItem(displayText, value));
                valueList.SetItemChecked(itemIndex, filterSelectionStates[value]);
            }
        }

        private static void SetVisibleFilterItemsChecked(
            CheckedListBox valueList,
            IDictionary<string, bool> filterSelectionStates,
            bool isChecked)
        {
            for (int i = 0; i < valueList.Items.Count; i++)
            {
                FilterValueItem item = valueList.Items[i] as FilterValueItem;
                if (item != null)
                {
                    filterSelectionStates[item.Value] = isChecked;
                    valueList.SetItemChecked(i, isChecked);
                }
            }
        }

        private static void SyncSelectAllCheckBox(CheckBox selectAllCheckBox, CheckedListBox valueList)
        {
            int checkedCount = valueList.CheckedItems.Count;
            selectAllCheckBox.Checked = valueList.Items.Count > 0 && checkedCount == valueList.Items.Count;
        }

        private void ApplyColumnFilter(
            string propertyName,
            IDictionary<string, bool> filterSelectionStates,
            IList<string> distinctValues)
        {
            var selectedValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, bool> selectionState in filterSelectionStates)
            {
                if (selectionState.Value)
                {
                    selectedValues.Add(selectionState.Key);
                }
            }

            if (selectedValues.Count == distinctValues.Count)
            {
                activeFilters.Remove(propertyName);
            }
            else
            {
                activeFilters[propertyName] = selectedValues;
            }

            ApplyFilters();
        }

        private IList<string> GetDistinctFilterValues(string propertyName)
        {
            var values = new List<string>();
            foreach (LightingSpaceRow row in allRows)
            {
                string value = GetRowPropertyText(row, propertyName);
                if (!values.Contains(value))
                {
                    values.Add(value);
                }
            }

            values.Sort(StringComparer.CurrentCultureIgnoreCase);
            return values;
        }

        private void ApplyFilters()
        {
            rows.RaiseListChangedEvents = false;
            rows.Clear();

            foreach (LightingSpaceRow row in allRows)
            {
                if (MatchesActiveFilters(row))
                {
                    rows.Add(row);
                }
            }

            rows.RaiseListChangedEvents = true;
            rows.ResetBindings();
            grid.Invalidate();
            UpdateTitle();
        }

        private bool MatchesActiveFilters(LightingSpaceRow row)
        {
            foreach (KeyValuePair<string, HashSet<string>> filter in activeFilters)
            {
                string cellText = GetRowPropertyText(row, filter.Key);
                if (!filter.Value.Contains(cellText))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetRowPropertyText(LightingSpaceRow row, string propertyName)
        {
            var property = typeof(LightingSpaceRow).GetProperty(propertyName);
            object value = property == null ? null : property.GetValue(row, null);
            return value == null ? string.Empty : value.ToString();
        }

        private sealed class FilterValueItem
        {
            public FilterValueItem(string displayText, string value)
            {
                DisplayText = displayText;
                Value = value;
            }

            public string DisplayText { get; private set; }

            public string Value { get; private set; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private void UpdateTitle()
        {
            titleLabel.Text = string.Format(
                "Lighting Calculation - {0} shown / {1} total Spaces / {2} fixture types",
                rows.Count,
                allRows.Count,
                fixtureTypes.Count);
        }

        private string ShowFilterInputDialog(string headerText, string currentValue)
        {
            using (var dialog = new System.Windows.Forms.Form())
            {
                dialog.Text = headerText + " Filter";
                dialog.Width = 360;
                dialog.Height = 140;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;

                var layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(12);
                layout.RowCount = 3;
                layout.ColumnCount = 2;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                dialog.Controls.Add(layout);

                var label = CreateTextLabel("Contains Text", ContentAlignment.MiddleLeft);
                layout.Controls.Add(label, 0, 0);
                layout.SetColumnSpan(label, 2);

                var input = new System.Windows.Forms.TextBox();
                input.Dock = DockStyle.Fill;
                input.Text = currentValue;
                layout.Controls.Add(input, 0, 1);
                layout.SetColumnSpan(input, 2);

                var okButton = CreateTextButton("OK", 88);
                okButton.DialogResult = DialogResult.OK;
                layout.Controls.Add(okButton, 0, 2);

                var cancelButton = CreateTextButton("Cancel", 88);
                cancelButton.DialogResult = DialogResult.Cancel;
                layout.Controls.Add(cancelButton, 1, 2);

                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }

        private void FixtureListItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (updatingFixtureList)
            {
                return;
            }

            BeginInvoke(new Action(delegate
            {
                IList<LightingSpaceRow> targetRows = GetFixtureTargetRows();
                if (targetRows.Count == 0 || e.NewValue != CheckState.Checked)
                {
                    return;
                }

                updatingFixtureList = true;
                for (int i = 0; i < fixtureList.Items.Count; i++)
                {
                    if (i != e.Index)
                    {
                        fixtureList.SetItemChecked(i, false);
                    }
                }
                updatingFixtureList = false;

                LightingFixtureType fixture = fixtureTypes[e.Index];
                foreach (LightingSpaceRow row in targetRows)
                {
                    row.FixtureType = fixture.Label;
                    if (fixture.FluxLm > 0)
                    {
                        row.FixtureFluxLm = fixture.FluxLm;
                    }

                    LightingCalculationService.Recalculate(row);
                    RefreshRowDisplay(row);
                }

                SyncFixtureChecks();
            }));
        }

        private IList<LightingSpaceRow> GetFixtureTargetRows()
        {
            IList<LightingSpaceRow> checkedRows = GetCheckedRows();
            if (checkedRows.Count > 0)
            {
                return checkedRows;
            }

            IList<LightingSpaceRow> selectedRows = GetGridSelectedRows();
            if (selectedRows.Count > 0)
            {
                return selectedRows;
            }

            return new List<LightingSpaceRow>();
        }

        private IList<LightingSpaceRow> GetCheckedRows()
        {
            var checkedRows = new List<LightingSpaceRow>();
            foreach (LightingSpaceRow row in rows)
            {
                if (row.IsSelected)
                {
                    checkedRows.Add(row);
                }
            }

            return checkedRows;
        }

        private IList<LightingSpaceRow> GetGridSelectedRows()
        {
            var selectedRows = new List<LightingSpaceRow>();
            foreach (DataGridViewRow gridRow in grid.SelectedRows)
            {
                LightingSpaceRow row = gridRow.DataBoundItem as LightingSpaceRow;
                if (row != null)
                {
                    selectedRows.Add(row);
                }
            }

            return selectedRows;
        }

        private IList<LightingSpaceRow> GetRowsFromSnapshot(IList<int> rowIndices)
        {
            var snapshotRows = new List<LightingSpaceRow>();
            foreach (int rowIndex in rowIndices)
            {
                if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
                {
                    continue;
                }

                LightingSpaceRow row = grid.Rows[rowIndex].DataBoundItem as LightingSpaceRow;
                if (row != null)
                {
                    snapshotRows.Add(row);
                }
            }

            return snapshotRows;
        }

        private LightingSpaceRow GetCurrentRow()
        {
            if (grid.CurrentRow == null)
            {
                if (grid.SelectedRows.Count > 0)
                {
                    return grid.SelectedRows[0].DataBoundItem as LightingSpaceRow;
                }

                return null;
            }

            return grid.CurrentRow.DataBoundItem as LightingSpaceRow;
        }

        private void SyncFixtureChecks()
        {
            if (fixtureList == null)
            {
                return;
            }

            LightingSpaceRow row = GetCurrentRow();
            updatingFixtureList = true;
            for (int i = 0; i < fixtureList.Items.Count; i++)
            {
                bool isChecked = false;
                if (row != null)
                {
                    LightingFixtureType fixture = fixtureTypes[i];
                    isChecked = string.Equals(fixture.Label, row.FixtureType, StringComparison.CurrentCultureIgnoreCase);
                }

                fixtureList.SetItemChecked(i, isChecked);
            }
            updatingFixtureList = false;
        }

        private void RecalculateAll()
        {
            CommitGridEdits();
            foreach (LightingSpaceRow row in rows)
            {
                LightingCalculationService.ApplyFixtureDefaults(row, fixtureTypes);
                LightingCalculationService.Recalculate(row);
            }

            grid.Refresh();
            SyncFixtureChecks();
        }

        private void RecalculateAllRows()
        {
            CommitGridEdits();
            foreach (LightingSpaceRow row in allRows)
            {
                LightingCalculationService.ApplyFixtureDefaults(row, fixtureTypes);
                LightingCalculationService.Recalculate(row);
            }

            grid.Refresh();
            SyncFixtureChecks();
        }

        private void RefreshRowDisplay(LightingSpaceRow row)
        {
            int rowIndex = rows.IndexOf(row);
            if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
            {
                grid.InvalidateRow(rowIndex);
            }
        }

        private void CommitGridEdits()
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

            grid.EndEdit();
            BindingContext[grid.DataSource].EndCurrentEdit();
        }

        private void FixtureListSelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingFixtureList)
            {
                return;
            }

            int index = fixtureList.SelectedIndex;
            if (index < 0 || index >= fixtureTypes.Count)
            {
                return;
            }

            LightingFixtureType fixture = fixtureTypes[index];
            fluxInputTextBox.Text = fixture.FluxLm > 0
                ? fixture.FluxLm.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private void WriteFluxButtonClick(object sender, EventArgs e)
        {
            int index = fixtureList.SelectedIndex;
            if (index < 0 || index >= fixtureTypes.Count)
            {
                TaskDialog.Show("조도 계산서", "조명 타입을 먼저 목록에서 선택하세요.");
                return;
            }

            double flux;
            if (!double.TryParse(fluxInputTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out flux) || flux <= 0)
            {
                if (!double.TryParse(fluxInputTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out flux) || flux <= 0)
                {
                    TaskDialog.Show("조도 계산서", "광속(lm) 값을 숫자로 입력하세요.\n예: 4000");
                    return;
                }
            }

            LightingFixtureType fixture = fixtureTypes[index];
            try
            {
                int count = LightingCalculationService.WriteFluxToTypeInstances(document, fixture.TypeId, flux);
                fixture.FluxLm = flux;
                fixture.FluxParameterName = LightingCalculationService.ResultFluxParam;
                RefreshFixtureListItem(index, fixture);
                RecalculateAll();
                TaskDialog.Show("조도 계산서",
                    string.Format("'{0}'\n기구 {1}개에 광속 {2:0.##} lm을 입력했습니다.", fixture.Label, count, flux));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("조도 계산서 - 오류", ex.ToString());
            }
        }

        private void RefreshFixtureListItem(int index, LightingFixtureType fixture)
        {
            updatingFixtureList = true;
            bool wasChecked = fixtureList.GetItemChecked(index);
            string fluxText = fixture.FluxLm > 0
                ? string.Format(CultureInfo.InvariantCulture, " ({0:0.##} lm)", fixture.FluxLm)
                : " (광속 없음)";
            fixtureList.Items[index] = fixture.Label + fluxText;
            fixtureList.SetItemChecked(index, wasChecked);
            updatingFixtureList = false;
        }

        private void SyncFluxButtonClick(object sender, EventArgs e)
        {
            try
            {
                int count = LightingCalculationService.SyncFixtureFluxToProject(document);
                string message = count > 0
                    ? string.Format("조명 기구 {0}개에 광속(fixtureFlux_lm)을 동기화했습니다.", count)
                    : "동기화할 광속값이 없거나 이미 모두 입력되어 있습니다.\n\n패밀리에 fixtureFlux_lm, 조도_광속_lm, 광속(lm) 등 광속 파라미터가 있는지 확인하세요.";
                TaskDialog.Show("조도 계산서 - 광속 동기화", message);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("조도 계산서 - 오류", ex.ToString());
            }
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {
            try
            {
                RecalculateAllRows();
                LightingCalculationService.SaveRows(document, allRows);
                TaskDialog.Show("조도 계산서", "Space 파라미터에 저장했습니다.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("조도 계산서 - 저장 오류", ex.ToString());
            }
            finally
            {
                SaveFormSettings();
            }
        }

        private void LightingCalculationFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                RecalculateAllRows();
                LightingCalculationService.SaveRows(document, allRows);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("조도 계산서 - 자동 저장 오류", ex.ToString());
            }
            finally
            {
                SaveFormSettings();
            }
        }

        private void ExportButtonClick(object sender, EventArgs e)
        {
            try
            {
                RecalculateAll();
                IList<LightingSpaceRow> exportRows = GetCheckedRows();
                if (exportRows.Count == 0)
                {
                    exportRows = GetGridSelectedRows();
                }
                if (exportRows.Count == 0)
                {
                    TaskDialog.Show("조도 계산서", "엑셀로 추출할 Space를 먼저 체크하거나 행으로 선택하세요.");
                    return;
                }

                string directory = exportPathTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(directory))
                {
                    TaskDialog.Show("조도 계산서", "엑셀 추출 경로를 입력하세요.");
                    return;
                }

                Directory.CreateDirectory(directory);
                string fileName = SanitizeFileName(document.Title) + "_조도계산_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";
                string filePath = Path.Combine(directory, fileName);
                SimpleXlsxWriter.Write(filePath, "조도계산", BuildExportMatrix(exportRows), BuildExportMergeRanges(), 2);
                TaskDialog.Show("조도 계산서", "엑셀 추출 완료:\n" + filePath);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("조도 계산서 - 엑셀 추출 오류", ex.ToString());
            }
        }

        private IList<IList<object>> BuildExportMatrix(IList<LightingSpaceRow> exportRows)
        {
            var matrix = new List<IList<object>>();
            matrix.Add(new List<object>
            {
                "ElementId", "레벨", "Space", "실의 조건", string.Empty, string.Empty, "등기구 사양", string.Empty,
                "조명 타입", "광속(lm)", "반사율", string.Empty, string.Empty, "조명율", "보수율", "요구조도(lx)",
                "계산등수", "필요등수", "계산조도(lx)"
            });
            matrix.Add(new List<object>
            {
                string.Empty, string.Empty, string.Empty, "면적(m2)", "가로(m)", "세로(m)", "광원고(m)", "실지수",
                string.Empty, string.Empty, "천정 반사율", "벽 반사율", "바닥 반사율", string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty
            });

            foreach (LightingSpaceRow row in exportRows)
            {
                matrix.Add(new List<object>
                {
                    row.SpaceId.IntegerValue, row.LevelName, row.SpaceName, Round(row.AreaM2), Round(row.LengthM), Round(row.WidthM),
                    Round(row.EffectiveHeightM), Round(row.RoomIndex), row.FixtureType, Round(row.FixtureFluxLm),
                    Round(row.CeilingReflectance), Round(row.WallReflectance), Round(row.FloorReflectance),
                    Round(row.UtilizationFactor), Round(row.MaintenanceFactor), Round(row.RequiredLux),
                    Round(row.RawRequiredCount), row.RequiredCount, Round(row.CalculatedIlluminance)
                });
            }

            return matrix;
        }

        private static IList<string> BuildExportMergeRanges()
        {
            return new[]
            {
                "A1:A2", "B1:B2", "C1:C2",
                "D1:F1", "G1:H1",
                "I1:I2", "J1:J2",
                "K1:M1",
                "N1:N2", "O1:O2", "P1:P2", "Q1:Q2", "R1:R2", "S1:S2"
            };
        }

        private static double Round(double value)
        {
            return Math.Round(value, 4);
        }

        private static string SanitizeFileName(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "RevitModel" : value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalid, '_');
            }

            return text;
        }

        private static string GetSettingsFilePath(Document document)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "LightingCalculationAddin");
            string modelName = document == null ? "RevitModel" : SanitizeFileName(document.Title);
            return Path.Combine(folder, modelName + ".settings");
        }

        private void LoadFormSettings()
        {
            Dictionary<string, string> settings = ReadSettingsFile(settingsFilePath);
            string value;

            if (settings.TryGetValue("ExportPath", out value) && !string.IsNullOrWhiteSpace(value))
            {
                exportPathTextBox.Text = value;
            }

            if (settings.TryGetValue("FluxInput", out value))
            {
                fluxInputTextBox.Text = value;
            }

            int width;
            int height;
            if (settings.TryGetValue("Width", out value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
                width >= MinimumSize.Width)
            {
                Width = width;
            }

            if (settings.TryGetValue("Height", out value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
                height >= MinimumSize.Height)
            {
                Height = height;
            }

            int splitterDistance;
            if (settings.TryGetValue("SplitterDistance", out value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out splitterDistance))
            {
                savedSplitterDistance = splitterDistance;
            }

            ApplySavedRowValues(settings);
            RecalculateAllRows();
        }

        private void SaveFormSettings()
        {
            CommitGridEdits();
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            settings["ExportPath"] = exportPathTextBox.Text;
            settings["FluxInput"] = fluxInputTextBox.Text;
            settings["Width"] = Width.ToString(CultureInfo.InvariantCulture);
            settings["Height"] = Height.ToString(CultureInfo.InvariantCulture);
            settings["SplitterDistance"] = split.SplitterDistance.ToString(CultureInfo.InvariantCulture);
            SaveRowsToSettings(settings);

            WriteSettingsFile(settingsFilePath, settings);
        }

        private void ApplySavedRowValues(IDictionary<string, string> settings)
        {
            foreach (LightingSpaceRow row in allRows)
            {
                string rowKey = GetRowSettingKey(row);
                double number;
                string text;

                if (TryReadDoubleSetting(settings, rowKey + ".RequiredLux", out number)) row.RequiredLux = number;
                if (TryReadDoubleSetting(settings, rowKey + ".EffectiveHeightM", out number)) row.EffectiveHeightM = number;
                if (TryReadDoubleSetting(settings, rowKey + ".CeilingReflectance", out number)) row.CeilingReflectance = number;
                if (TryReadDoubleSetting(settings, rowKey + ".WallReflectance", out number)) row.WallReflectance = number;
                if (TryReadDoubleSetting(settings, rowKey + ".FloorReflectance", out number)) row.FloorReflectance = number;
                if (TryReadDoubleSetting(settings, rowKey + ".FixtureFluxLm", out number)) row.FixtureFluxLm = number;
                if (settings.TryGetValue(rowKey + ".FixtureType", out text)) row.FixtureType = text;
            }
        }

        private void SaveRowsToSettings(IDictionary<string, string> settings)
        {
            foreach (LightingSpaceRow row in allRows)
            {
                string rowKey = GetRowSettingKey(row);
                settings[rowKey + ".RequiredLux"] = row.RequiredLux.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".EffectiveHeightM"] = row.EffectiveHeightM.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".CeilingReflectance"] = row.CeilingReflectance.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".WallReflectance"] = row.WallReflectance.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".FloorReflectance"] = row.FloorReflectance.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".MaintenanceFactor"] = row.MaintenanceFactor.ToString(CultureInfo.InvariantCulture);
                settings[rowKey + ".FixtureType"] = row.FixtureType ?? string.Empty;
                settings[rowKey + ".FixtureFluxLm"] = row.FixtureFluxLm.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool TryReadDoubleSetting(IDictionary<string, string> settings, string key, out double value)
        {
            string text;
            if (settings.TryGetValue(key, out text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static string GetRowSettingKey(LightingSpaceRow row)
        {
            return "Row." + row.SpaceId.IntegerValue.ToString(CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, string> ReadSettingsFile(string path)
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return settings;
            }

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator);
                string encodedValue = line.Substring(separator + 1);
                settings[key] = DecodeSettingValue(encodedValue);
            }

            return settings;
        }

        private static void WriteSettingsFile(string path, IDictionary<string, string> settings)
        {
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var lines = new List<string>();
            foreach (KeyValuePair<string, string> item in settings)
            {
                lines.Add(item.Key + "=" + EncodeSettingValue(item.Value));
            }

            File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        private static string EncodeSettingValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string DecodeSettingValue(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetDefaultExportDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "조도계산서_exports");
        }

        private void BrowseExportPathButtonClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "엑셀 추출 폴더를 선택하세요.";
                dialog.SelectedPath = Directory.Exists(exportPathTextBox.Text)
                    ? exportPathTextBox.Text
                    : GetDefaultExportDirectory();

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    exportPathTextBox.Text = dialog.SelectedPath;
                }
            }
        }
    }
}
