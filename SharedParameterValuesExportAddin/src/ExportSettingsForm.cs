using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public class ExportSettingsForm : System.Windows.Forms.Form
    {
        private readonly Document document;
        private readonly CheckedListBox categoryList = new CheckedListBox();
        private readonly ListBox availableParameterList = new ListBox();
        private readonly ListBox exportParameterList = new ListBox();
        private readonly TextBox outputPathTextBox = new TextBox();
        private readonly TextBox sheetNameTextBox = new TextBox();
        private readonly IList<Category> categories;
        private bool suppressCategoryCheckRefresh;
        private static readonly Font EnglishUiFont = new Font("Arial", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font KoreanUiFont = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font TitleUiFont = new Font("Arial", 10F, FontStyle.Bold, GraphicsUnit.Point);

        public ExportOptions Options { get; private set; }

        public ExportSettingsForm(Document document)
        {
            this.document = document;
            Text = "Shared Parameter Values Export";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 720);
            Size = new Size(1120, 760);
            Font = EnglishUiFont;

            var usedCategoryIds = GetUsedCategoryIds(document);

            categories = document.Settings.Categories
                .Cast<Category>()
                .Where(category =>
                    category != null &&
                    category.AllowsBoundParameters &&
                    usedCategoryIds.Contains(GetElementIdValue(category.Id)))
                .OrderBy(category => category.Name)
                .ToList();

            BuildLayout();
            LoadCategories();
            LoadParametersForSelectedCategories();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Controls.Add(root);

            outputPathTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "codex",
                "다이나모");
            sheetNameTextBox.Text = "SharedParameters";

            root.Controls.Add(CreateOutputPathPanel(), 0, 0);
            root.Controls.Add(CreateTextField("Sheet name", sheetNameTextBox), 0, 1);

            root.Controls.Add(CreateSelectionPanel(), 0, 2);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            var exportButton = new Button { Text = "Export", Width = 96, DialogResult = DialogResult.OK };
            exportButton.Click += ExportButton_Click;
            var cancelButton = new Button { Text = "Cancel", Width = 96, DialogResult = DialogResult.Cancel };
            buttonPanel.Controls.Add(exportButton);
            buttonPanel.Controls.Add(cancelButton);
            root.Controls.Add(buttonPanel, 0, 3);

            AcceptButton = exportButton;
            CancelButton = cancelButton;
        }

        private System.Windows.Forms.Control CreateOutputPathPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var field = CreateTextField("Output folder or .xlsx file path", outputPathTextBox);
            var browseButton = new Button { Text = "Browse...", Width = 92, Height = 24, Margin = new Padding(8, 22, 0, 0) };
            browseButton.Click += BrowseButton_Click;

            panel.Controls.Add(field, 0, 0);
            panel.Controls.Add(browseButton, 1, 0);
            return panel;
        }

        private System.Windows.Forms.Control CreateSelectionPanel()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            categoryList.Dock = DockStyle.Fill;
            categoryList.CheckOnClick = true;
            categoryList.IntegralHeight = false;
            categoryList.ItemCheck += CategoryList_ItemCheck;

            availableParameterList.Dock = DockStyle.Fill;
            availableParameterList.IntegralHeight = false;
            availableParameterList.SelectionMode = SelectionMode.MultiExtended;
            availableParameterList.DrawMode = DrawMode.OwnerDrawFixed;
            availableParameterList.ItemHeight = Math.Max(availableParameterList.ItemHeight, 20);
            availableParameterList.DrawItem += ParameterList_DrawItem;
            availableParameterList.DoubleClick += (sender, args) => AddSelectedParameters();

            exportParameterList.Dock = DockStyle.Fill;
            exportParameterList.IntegralHeight = false;
            exportParameterList.SelectionMode = SelectionMode.MultiExtended;
            exportParameterList.DrawMode = DrawMode.OwnerDrawFixed;
            exportParameterList.ItemHeight = Math.Max(exportParameterList.ItemHeight, 20);
            exportParameterList.DrawItem += ParameterList_DrawItem;
            exportParameterList.DoubleClick += (sender, args) => RemoveSelectedExportParameters();

            layout.Controls.Add(CreateCategoryPanel(), 0, 0);
            layout.Controls.Add(CreateAvailableParameterPanel(), 1, 0);
            layout.Controls.Add(CreateMoveButtonPanel(), 2, 0);
            layout.Controls.Add(CreateExportParameterPanel(), 3, 0);
            layout.Controls.Add(CreateOrderButtonPanel(), 4, 0);
            return layout;
        }

        private System.Windows.Forms.Control CreateCategoryPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            var selectAllButton = new Button { Text = "Select all", Width = 86 };
            selectAllButton.Click += (sender, args) => SetAllCategoriesChecked(true);
            var clearButton = new Button { Text = "Clear", Width = 86 };
            clearButton.Click += (sender, args) => SetAllCategoriesChecked(false);
            buttons.Controls.Add(selectAllButton);
            buttons.Controls.Add(clearButton);

            layout.Controls.Add(buttons, 0, 0);
            layout.Controls.Add(categoryList, 0, 1);
            return CreateTitledPanel("Categories", layout);
        }

        private System.Windows.Forms.Control CreateAvailableParameterPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(CreateLegendLabel(), 0, 0);
            layout.Controls.Add(availableParameterList, 0, 1);
            return CreateTitledPanel("Shared Parameters", layout);
        }

        private System.Windows.Forms.Control CreateExportParameterPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(CreateLegendLabel(), 0, 0);
            layout.Controls.Add(exportParameterList, 0, 1);
            return CreateTitledPanel("Export Parameters", layout);
        }

        private static System.Windows.Forms.Control CreateTitledPanel(string titleText, System.Windows.Forms.Control content)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle
            };

            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0, 4, 0, 0);

            panel.Controls.Add(CreateTitleLabel(titleText, true), 0, 0);
            panel.Controls.Add(content, 0, 1);
            return panel;
        }

        private static Label CreateTitleLabel(string text, bool showBorder)
        {
            return new Label
            {
                AutoSize = true,
                BorderStyle = showBorder ? BorderStyle.FixedSingle : BorderStyle.None,
                Font = TitleUiFont,
                Padding = new Padding(4, 2, 4, 2),
                Margin = new Padding(0, 0, 0, 4),
                Text = text
            };
        }

        private System.Windows.Forms.Control CreateLegendLabel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 4)
            };

            panel.Controls.Add(CreateLegendColorBox(ParameterOrigin.Project));
            panel.Controls.Add(CreateLegendTextLabel("Project parameter"));
            panel.Controls.Add(CreateLegendColorBox(ParameterOrigin.Family));
            panel.Controls.Add(CreateLegendTextLabel("Family parameter"));

            return panel;
        }

        private static System.Windows.Forms.Control CreateLegendColorBox(ParameterOrigin origin)
        {
            return new System.Windows.Forms.Panel
            {
                BackColor = GetParameterOriginColor(origin),
                Width = 12,
                Height = 12,
                Margin = new Padding(0, 3, 4, 0)
            };
        }

        private static Label CreateLegendTextLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = EnglishUiFont,
                Margin = new Padding(0, 0, 12, 0),
                Text = text
            };
        }

        private System.Windows.Forms.Control CreateMoveButtonPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Padding = new Padding(6, 60, 6, 0)
            };

            var addButton = new Button { Text = ">", Width = 44 };
            addButton.Click += (sender, args) => AddSelectedParameters();
            var addAllButton = new Button { Text = ">>", Width = 44 };
            addAllButton.Click += (sender, args) => AddAllAvailableParameters();
            var removeButton = new Button { Text = "<", Width = 44 };
            removeButton.Click += (sender, args) => RemoveSelectedExportParameters();
            var removeAllButton = new Button { Text = "<<", Width = 44 };
            removeAllButton.Click += (sender, args) =>
            {
                exportParameterList.Items.Clear();
                LoadParametersForSelectedCategories();
            };

            panel.Controls.Add(addButton);
            panel.Controls.Add(addAllButton);
            panel.Controls.Add(removeButton);
            panel.Controls.Add(removeAllButton);
            return panel;
        }

        private System.Windows.Forms.Control CreateOrderButtonPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Padding = new Padding(6, 60, 0, 0)
            };

            var upButton = new Button { Text = "Up", Width = 54 };
            upButton.Click += (sender, args) => MoveSelectedExportParameter(-1);
            var downButton = new Button { Text = "Down", Width = 54 };
            downButton.Click += (sender, args) => MoveSelectedExportParameter(1);

            panel.Controls.Add(upButton);
            panel.Controls.Add(downButton);
            return panel;
        }

        private static System.Windows.Forms.Control CreateTextField(string labelText, TextBox textBox)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, RowCount = 2, ColumnCount = 1, AutoSize = true };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(CreateTitleLabel(labelText, false), 0, 0);
            textBox.Dock = DockStyle.Top;
            panel.Controls.Add(textBox, 0, 1);
            return panel;
        }

        private void LoadCategories()
        {
            var defaultBuiltInNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "OST_ElectricalEquipment",
                "OST_CableTrayFitting",
                "OST_CableTray",
                "OST_LightingFixtures",
                "OST_ElectricalFixtures"
            };

            suppressCategoryCheckRefresh = true;
            try
            {
                foreach (Category category in categories)
                {
                    int index = categoryList.Items.Add(new CategoryListItem(category));
                    if (defaultBuiltInNames.Contains(GetBuiltInCategoryName(category)))
                    {
                        categoryList.SetItemChecked(index, true);
                    }
                }
            }
            finally
            {
                suppressCategoryCheckRefresh = false;
            }
        }

        private void LoadParametersForSelectedCategories()
        {
            IList<Category> selectedCategories = GetSelectedCategories();
            IList<ParameterSelection> parameters;

            try
            {
                parameters = ParameterExportService.DiscoverSharedParameters(document, selectedCategories);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                parameters = new List<ParameterSelection>();
            }

            availableParameterList.Items.Clear();
            foreach (ParameterSelection parameter in parameters)
            {
                var item = new ParameterListItem(parameter);
                if (!ContainsParameter(exportParameterList, item.Key))
                {
                    availableParameterList.Items.Add(item);
                }
            }
        }

        private void CategoryList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (suppressCategoryCheckRefresh)
            {
                return;
            }

            BeginInvoke((Action)LoadParametersForSelectedCategories);
        }

        private void AddSelectedParameters()
        {
            var items = availableParameterList.SelectedItems.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                AddExportParameter(item);
                availableParameterList.Items.Remove(item);
            }
        }

        private void AddAllAvailableParameters()
        {
            var items = availableParameterList.Items.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                AddExportParameter(item);
            }

            availableParameterList.Items.Clear();
        }

        private void AddExportParameter(ParameterListItem item)
        {
            if (!ContainsParameter(exportParameterList, item.Key))
            {
                exportParameterList.Items.Add(item);
            }
        }

        private void RemoveSelectedExportParameters()
        {
            var items = exportParameterList.SelectedItems.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                exportParameterList.Items.Remove(item);
            }

            LoadParametersForSelectedCategories();
        }

        private void SetAllCategoriesChecked(bool isChecked)
        {
            suppressCategoryCheckRefresh = true;
            try
            {
                for (int index = 0; index < categoryList.Items.Count; index++)
                {
                    categoryList.SetItemChecked(index, isChecked);
                }
            }
            finally
            {
                suppressCategoryCheckRefresh = false;
            }

            LoadParametersForSelectedCategories();
        }

        private void MoveSelectedExportParameter(int direction)
        {
            if (exportParameterList.SelectedItem == null)
            {
                return;
            }

            int index = exportParameterList.SelectedIndex;
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= exportParameterList.Items.Count)
            {
                return;
            }

            object item = exportParameterList.SelectedItem;
            exportParameterList.Items.RemoveAt(index);
            exportParameterList.Items.Insert(newIndex, item);
            exportParameterList.SelectedIndex = newIndex;
        }

        private void ParameterList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            ListBox listBox = sender as ListBox;
            ParameterListItem item = listBox.Items[e.Index] as ParameterListItem;
            if (item == null)
            {
                return;
            }

            e.DrawBackground();

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            System.Drawing.Color textColor = GetParameterOriginColor(item.Selection.Origin);
            if (isSelected)
            {
                textColor = SystemColors.HighlightText;
            }

            System.Drawing.Rectangle markerBounds = new System.Drawing.Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 5, 10, 10);
            using (Brush markerBrush = new SolidBrush(GetParameterOriginColor(item.Selection.Origin)))
            {
                e.Graphics.FillRectangle(markerBrush, markerBounds);
            }

            System.Drawing.Rectangle textBounds = new System.Drawing.Rectangle(
                e.Bounds.Left + 18,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 20),
                e.Bounds.Height);

            using (Brush brush = new SolidBrush(textColor))
            {
                DrawMixedLanguageString(e.Graphics, item.ToString(), brush, textBounds);
            }

            e.DrawFocusRectangle();
        }

        private static void DrawMixedLanguageString(Graphics graphics, string text, Brush brush, System.Drawing.Rectangle bounds)
        {
            float x = bounds.Left;
            float y = bounds.Top + Math.Max(0, (bounds.Height - Math.Max(EnglishUiFont.Height, KoreanUiFont.Height)) / 2);

            foreach (string run in SplitLanguageRuns(text))
            {
                Font font = ContainsKorean(run) ? KoreanUiFont : EnglishUiFont;
                graphics.DrawString(run, font, brush, x, y);
                x += graphics.MeasureString(run, font, bounds.Width, StringFormat.GenericTypographic).Width;
                if (x >= bounds.Right)
                {
                    break;
                }
            }
        }

        private static IEnumerable<string> SplitLanguageRuns(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            int startIndex = 0;
            bool currentIsKorean = IsKorean(text[0]);

            for (int index = 1; index < text.Length; index++)
            {
                bool isKorean = IsKorean(text[index]);
                if (isKorean != currentIsKorean)
                {
                    yield return text.Substring(startIndex, index - startIndex);
                    startIndex = index;
                    currentIsKorean = isKorean;
                }
            }

            yield return text.Substring(startIndex);
        }

        private static bool ContainsKorean(string text)
        {
            return text.Any(IsKorean);
        }

        private static bool IsKorean(char value)
        {
            return (value >= 0xAC00 && value <= 0xD7AF) ||
                   (value >= 0x1100 && value <= 0x11FF) ||
                   (value >= 0x3130 && value <= 0x318F) ||
                   (value >= 0xA960 && value <= 0xA97F) ||
                   (value >= 0xD7B0 && value <= 0xD7FF);
        }

        private static System.Drawing.Color GetParameterOriginColor(ParameterOrigin origin)
        {
            return origin == ParameterOrigin.Project
                ? System.Drawing.Color.RoyalBlue
                : System.Drawing.Color.ForestGreen;
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select an output folder";
                dialog.SelectedPath = Directory.Exists(outputPathTextBox.Text)
                    ? outputPathTextBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputPathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            IList<Category> selectedCategories = GetSelectedCategories();

            if (selectedCategories.Count == 0)
            {
                MessageBox.Show(this, "Select at least one Revit category.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            var selectedParameters = exportParameterList.Items
                .Cast<ParameterListItem>()
                .Select(item => item.Selection)
                .ToList();

            if (selectedParameters.Count == 0)
            {
                MessageBox.Show(this, "Select at least one shared parameter to export.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Options = new ExportOptions
            {
                OutputPath = outputPathTextBox.Text,
                SheetName = sheetNameTextBox.Text,
                Categories = selectedCategories,
                Parameters = selectedParameters
            };
        }

        private IList<Category> GetSelectedCategories()
        {
            return categoryList.CheckedItems
                .Cast<CategoryListItem>()
                .Select(item => item.Category)
                .ToList();
        }

        private static bool ContainsParameter(ListBox listBox, string key)
        {
            foreach (ParameterListItem item in listBox.Items)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private class CategoryListItem
        {
            public CategoryListItem(Category category)
            {
                Category = category;
            }

            public Category Category { get; private set; }

            public override string ToString()
            {
                return Category.Name;
            }
        }

        private class ParameterListItem
        {
            public ParameterListItem(ParameterSelection selection)
            {
                Selection = selection;
            }

            public ParameterSelection Selection { get; private set; }

            public string Key
            {
                get
                {
                    return string.Format("{0}|{1}", Selection.Scope, Selection.Name);
                }
            }

            public override string ToString()
            {
                return Selection.DisplayName;
            }
        }

        private static string GetBuiltInCategoryName(Category category)
        {
            try
            {
                return ((BuiltInCategory)GetElementIdValue(category.Id)).ToString();
            }
            catch
            {
                return GetElementIdValue(category.Id).ToString();
            }
        }

        private static HashSet<long> GetUsedCategoryIds(Document document)
        {
            var categoryIds = new HashSet<long>();

            foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
            {
                try
                {
                    if (element.Category != null)
                    {
                        categoryIds.Add(GetElementIdValue(element.Category.Id));
                    }
                }
                catch
                {
                    continue;
                }
            }

            return categoryIds;
        }

        private static long GetElementIdValue(ElementId id)
        {
#if REVIT2024_OR_LESS
#pragma warning disable 618
            return id.IntegerValue;
#pragma warning restore 618
#else
            return id.Value;
#endif
        }
    }
}
