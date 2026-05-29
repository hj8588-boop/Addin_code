using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public class ImportSettingsForm : System.Windows.Forms.Form
    {
        private readonly TextBox filePathTextBox = new TextBox();
        private readonly TextBox sheetNameTextBox = new TextBox();
        private readonly CheckedListBox categoryList = new CheckedListBox();
        private readonly ListBox availableParameterList = new ListBox();
        private readonly ListBox importParameterList = new ListBox();
        private readonly IList<Category> categories;
        private static readonly Font EnglishUiFont = new Font("Arial", 9F, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font TitleUiFont = new Font("Arial", 10F, FontStyle.Bold, GraphicsUnit.Point);

        public ImportOptions Options { get; private set; }

        public ImportSettingsForm(Document document)
        {
            Text = "Shared Parameter Values Import";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(680, 620);
            Size = new Size(760, 680);
            Font = EnglishUiFont;

            categories = GetUsedCategories(document);
            BuildLayout();
            LoadCategories();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            sheetNameTextBox.Text = "SharedParameters";
            categoryList.Dock = DockStyle.Fill;
            categoryList.CheckOnClick = true;
            categoryList.IntegralHeight = false;
            availableParameterList.Dock = DockStyle.Fill;
            availableParameterList.IntegralHeight = false;
            availableParameterList.SelectionMode = SelectionMode.MultiExtended;
            availableParameterList.DoubleClick += (sender, args) => AddSelectedParameters();
            importParameterList.Dock = DockStyle.Fill;
            importParameterList.IntegralHeight = false;
            importParameterList.SelectionMode = SelectionMode.MultiExtended;
            importParameterList.DoubleClick += (sender, args) => RemoveSelectedImportParameters();
            filePathTextBox.Leave += (sender, args) => LoadImportParameters();
            sheetNameTextBox.Leave += (sender, args) => LoadImportParameters();

            root.Controls.Add(CreateFilePathPanel(), 0, 0);
            root.Controls.Add(CreateTextField("Sheet name (.xlsx/.xlsm only)", sheetNameTextBox), 0, 1);
            root.Controls.Add(CreateSelectionPanel(), 0, 2);
            root.Controls.Add(CreateWarningLabel(), 0, 3);
            root.Controls.Add(CreateButtonPanel(), 0, 4);
        }

        private System.Windows.Forms.Control CreateFilePathPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var field = CreateTextField("CSV / Excel file path", filePathTextBox);
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
                ColumnCount = 4,
                RowCount = 1
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));

            layout.Controls.Add(CreateCategoryPanel(), 0, 0);
            layout.Controls.Add(CreateAvailableParameterPanel(), 1, 0);
            layout.Controls.Add(CreateMoveButtonPanel(), 2, 0);
            layout.Controls.Add(CreateImportParameterPanel(), 3, 0);
            return layout;
        }

        private System.Windows.Forms.Control CreateCategoryPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            layout.Controls.Add(new Label
            {
                Text = "Only checked categories will be overwritten.",
                Dock = DockStyle.Top,
                AutoSize = true
            }, 0, 1);
            layout.Controls.Add(categoryList, 0, 2);
            return CreateTitledPanel("Categories", layout);
        }

        private System.Windows.Forms.Control CreateAvailableParameterPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label
            {
                Text = "Loaded from selected import file.",
                Dock = DockStyle.Top,
                AutoSize = true
            }, 0, 0);
            layout.Controls.Add(availableParameterList, 0, 1);
            return CreateTitledPanel("Available Parameters", layout);
        }

        private System.Windows.Forms.Control CreateImportParameterPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(importParameterList, 0, 0);
            return CreateTitledPanel("Import Parameters", layout);
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
            removeButton.Click += (sender, args) => RemoveSelectedImportParameters();
            var removeAllButton = new Button { Text = "<<", Width = 44 };
            removeAllButton.Click += (sender, args) =>
            {
                importParameterList.Items.Clear();
                LoadImportParameters();
            };

            panel.Controls.Add(addButton);
            panel.Controls.Add(addAllButton);
            panel.Controls.Add(removeButton);
            panel.Controls.Add(removeAllButton);
            return panel;
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

        private System.Windows.Forms.Control CreateWarningLabel()
        {
            return new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = System.Drawing.Color.DarkRed,
                Text = "Import overwrites Revit parameter values. Save or backup the model before running."
            };
        }

        private System.Windows.Forms.Control CreateButtonPanel()
        {
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            var importButton = new Button { Text = "Import", Width = 96, DialogResult = DialogResult.OK };
            importButton.Click += ImportButton_Click;
            var cancelButton = new Button { Text = "Cancel", Width = 96, DialogResult = DialogResult.Cancel };
            buttonPanel.Controls.Add(importButton);
            buttonPanel.Controls.Add(cancelButton);

            AcceptButton = importButton;
            CancelButton = cancelButton;
            return buttonPanel;
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
            foreach (Category category in categories)
            {
                categoryList.Items.Add(new CategoryListItem(category));
            }
        }

        private void LoadImportParameters()
        {
            availableParameterList.Items.Clear();

            if (string.IsNullOrWhiteSpace(filePathTextBox.Text) || !File.Exists(filePathTextBox.Text))
            {
                return;
            }

            IList<ParameterSelection> parameters;
            try
            {
                parameters = ParameterImportService.DiscoverImportParameters(filePathTextBox.Text, sheetNameTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (ParameterSelection parameter in parameters)
            {
                var item = new ParameterListItem(parameter);
                if (!ContainsParameter(importParameterList, item.Key))
                {
                    availableParameterList.Items.Add(item);
                }
            }
        }

        private void AddSelectedParameters()
        {
            var items = availableParameterList.SelectedItems.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                AddImportParameter(item);
                availableParameterList.Items.Remove(item);
            }
        }

        private void AddAllAvailableParameters()
        {
            var items = availableParameterList.Items.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                AddImportParameter(item);
            }

            availableParameterList.Items.Clear();
        }

        private void AddImportParameter(ParameterListItem item)
        {
            if (!ContainsParameter(importParameterList, item.Key))
            {
                importParameterList.Items.Add(item);
            }
        }

        private void RemoveSelectedImportParameters()
        {
            var items = importParameterList.SelectedItems.Cast<ParameterListItem>().ToList();
            foreach (ParameterListItem item in items)
            {
                importParameterList.Items.Remove(item);
            }

            LoadImportParameters();
        }

        private void SetAllCategoriesChecked(bool isChecked)
        {
            for (int index = 0; index < categoryList.Items.Count; index++)
            {
                categoryList.SetItemChecked(index, isChecked);
            }
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select CSV or Excel file";
                dialog.Filter = "Spreadsheet files (*.csv;*.xlsx;*.xlsm)|*.csv;*.xlsx;*.xlsm|All files (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    filePathTextBox.Text = dialog.FileName;
                    LoadImportParameters();
                }
            }
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            if (!File.Exists(filePathTextBox.Text))
            {
                MessageBox.Show(this, "Select an existing CSV or Excel file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            IList<Category> selectedCategories = categoryList.CheckedItems
                .Cast<CategoryListItem>()
                .Select(item => item.Category)
                .ToList();

            if (selectedCategories.Count == 0)
            {
                MessageBox.Show(this, "Select at least one category to import.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            var selectedParameters = importParameterList.Items
                .Cast<ParameterListItem>()
                .Select(item => item.Selection)
                .ToList();

            if (selectedParameters.Count == 0)
            {
                MessageBox.Show(this, "Select at least one parameter to import.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                string.Format(
                    "This will overwrite {0} selected parameter(s) only for the {1} checked category/categories.\n\nContinue?",
                    selectedParameters.Count,
                    selectedCategories.Count),
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmation != DialogResult.Yes)
            {
                DialogResult = DialogResult.None;
                return;
            }

            Options = new ImportOptions
            {
                FilePath = filePathTextBox.Text,
                SheetName = sheetNameTextBox.Text,
                Categories = selectedCategories,
                Parameters = selectedParameters
            };
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

        private static IList<Category> GetUsedCategories(Document document)
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

            return document.Settings.Categories
                .Cast<Category>()
                .Where(category =>
                    category != null &&
                    category.AllowsBoundParameters &&
                    categoryIds.Contains(GetElementIdValue(category.Id)))
                .OrderBy(category => category.Name)
                .ToList();
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
    }
}
