using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public static class ParameterImportService
    {
        public static ImportResult Import(Document document, ImportOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.FilePath))
            {
                throw new InvalidOperationException("Import file path is empty.");
            }

            IList<IList<string>> matrix = SpreadsheetReader.Read(options.FilePath, options.SheetName);
            if (matrix.Count < 2)
            {
                throw new InvalidOperationException("The import file does not contain any data rows.");
            }

            IDictionary<string, int> headerIndex = BuildHeaderIndex(matrix[0]);
            IList<ParameterColumn> parameterColumns = GetParameterColumns(matrix[0], options.Parameters);
            HashSet<long> allowedCategoryIds = new HashSet<long>(
                options.Categories
                    .Where(category => category != null)
                    .Select(category => GetElementIdValue(category.Id)));

            var failed = new List<string>();
            var skipped = new List<string>();
            int updatedElementCount = 0;
            int updatedValueCount = 0;

            using (var transaction = new Transaction(document, "Import shared parameter values"))
            {
                transaction.Start();

                for (int rowIndex = 1; rowIndex < matrix.Count; rowIndex++)
                {
                    IList<string> row = matrix[rowIndex];
                    Element element = GetElementFromRow(document, row, headerIndex);
                    if (element == null)
                    {
                        skipped.Add(string.Format("Row {0}: element not found", rowIndex + 1));
                        continue;
                    }

                    if (!IsAllowedCategory(element, allowedCategoryIds))
                    {
                        skipped.Add(string.Format("Row {0}: category not selected", rowIndex + 1));
                        continue;
                    }

                    int rowUpdatedCount = 0;
                    foreach (ParameterColumn column in parameterColumns)
                    {
                        if (column.Index >= row.Count)
                        {
                            continue;
                        }

                        string rawValue = Normalize(row[column.Index]);
                        if (rawValue.Length == 0)
                        {
                            continue;
                        }

                        Parameter parameter = GetParameter(document, element, column.Selection);
                        if (parameter == null)
                        {
                            failed.Add(string.Format("Row {0}: parameter not found [{1}]", rowIndex + 1, column.Selection.DisplayName));
                            continue;
                        }

                        if (!parameter.IsShared)
                        {
                            failed.Add(string.Format("Row {0}: parameter is not shared [{1}]", rowIndex + 1, column.Selection.DisplayName));
                            continue;
                        }

                        if (parameter.IsReadOnly)
                        {
                            failed.Add(string.Format("Row {0}: parameter is read-only [{1}]", rowIndex + 1, column.Selection.DisplayName));
                            continue;
                        }

                        try
                        {
                            if (SetParameterValue(parameter, rawValue))
                            {
                                rowUpdatedCount++;
                                updatedValueCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed.Add(string.Format("Row {0}: {1} [{2}]", rowIndex + 1, ex.Message, column.Selection.DisplayName));
                        }
                    }

                    if (rowUpdatedCount > 0)
                    {
                        updatedElementCount++;
                    }
                }

                transaction.Commit();
            }

            return new ImportResult
            {
                FilePath = options.FilePath,
                SheetName = options.SheetName,
                UpdatedElementCount = updatedElementCount,
                UpdatedValueCount = updatedValueCount,
                Skipped = skipped,
                Failed = failed
            };
        }

        public static IList<ParameterSelection> DiscoverImportParameters(string filePath, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new List<ParameterSelection>();
            }

            IList<IList<string>> matrix = SpreadsheetReader.Read(filePath, sheetName);
            if (matrix.Count == 0)
            {
                return new List<ParameterSelection>();
            }

            return GetParameterColumns(matrix[0], null)
                .Select(column => column.Selection)
                .ToList();
        }

        private static IDictionary<string, int> BuildHeaderIndex(IList<string> headers)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                string header = Normalize(headers[columnIndex]);
                if (header.Length > 0 && !index.ContainsKey(header))
                {
                    index.Add(header, columnIndex);
                }
            }

            return index;
        }

        private static IList<ParameterColumn> GetParameterColumns(IList<string> headers, IList<ParameterSelection> selectedParameters)
        {
            var columns = new List<ParameterColumn>();
            var fixedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ElementId",
                "UniqueId",
                "Category",
                "Family",
                "Type"
            };

            for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                string header = Normalize(headers[columnIndex]);
                if (header.Length == 0 || fixedHeaders.Contains(header))
                {
                    continue;
                }

                ParameterSelection selection = ParseParameterHeader(header);
                if (selectedParameters != null && selectedParameters.Count > 0 && !ContainsSelection(selectedParameters, selection))
                {
                    continue;
                }

                columns.Add(new ParameterColumn(columnIndex, selection));
            }

            return columns;
        }

        private static bool ContainsSelection(IList<ParameterSelection> selections, ParameterSelection selection)
        {
            foreach (ParameterSelection item in selections)
            {
                if (item != null &&
                    string.Equals(item.Name, selection.Name, StringComparison.OrdinalIgnoreCase) &&
                    item.Scope == selection.Scope)
                {
                    return true;
                }
            }

            return false;
        }

        private static ParameterSelection ParseParameterHeader(string header)
        {
            if (header.EndsWith("[Type]", StringComparison.OrdinalIgnoreCase))
            {
                return new ParameterSelection(header.Substring(0, header.Length - 6).Trim(), ParameterScope.Type);
            }

            if (header.EndsWith("[Instance]", StringComparison.OrdinalIgnoreCase))
            {
                return new ParameterSelection(header.Substring(0, header.Length - 10).Trim(), ParameterScope.Instance);
            }

            return new ParameterSelection(header, ParameterScope.Instance);
        }

        private static Element GetElementFromRow(Document document, IList<string> row, IDictionary<string, int> headerIndex)
        {
            int uniqueIdIndex;
            if (headerIndex.TryGetValue("UniqueId", out uniqueIdIndex) && uniqueIdIndex < row.Count)
            {
                string uniqueId = Normalize(row[uniqueIdIndex]);
                if (uniqueId.Length > 0)
                {
                    try
                    {
                        Element element = document.GetElement(uniqueId);
                        if (element != null)
                        {
                            return element;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            int elementIdIndex;
            if (headerIndex.TryGetValue("ElementId", out elementIdIndex) && elementIdIndex < row.Count)
            {
                int elementId;
                if (TryParseInteger(row[elementIdIndex], out elementId))
                {
#pragma warning disable 618
                    return document.GetElement(new ElementId(elementId));
#pragma warning restore 618
                }
            }

            return null;
        }

        private static bool IsAllowedCategory(Element element, HashSet<long> allowedCategoryIds)
        {
            try
            {
                return element.Category != null && allowedCategoryIds.Contains(GetElementIdValue(element.Category.Id));
            }
            catch
            {
                return false;
            }
        }

        private static Parameter GetParameter(Document document, Element element, ParameterSelection selection)
        {
            Element sourceElement = selection.Scope == ParameterScope.Type
                ? GetElementType(document, element)
                : element;

            return sourceElement == null ? null : sourceElement.LookupParameter(selection.Name);
        }

        private static Element GetElementType(Document document, Element element)
        {
            try
            {
                ElementId typeId = element.GetTypeId();
                if (typeId == ElementId.InvalidElementId)
                {
                    return null;
                }

                return document.GetElement(typeId);
            }
            catch
            {
                return null;
            }
        }

        private static bool SetParameterValue(Parameter parameter, string rawValue)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(rawValue);
                    return true;

                case StorageType.Integer:
                    int integerValue;
                    if (!TryParseInteger(rawValue, out integerValue))
                    {
                        throw new InvalidOperationException("Integer value expected: " + rawValue);
                    }

                    parameter.Set(integerValue);
                    return true;

                case StorageType.Double:
                    try
                    {
                        if (parameter.SetValueString(rawValue))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }

                    double doubleValue;
                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                    {
                        throw new InvalidOperationException("Number value expected: " + rawValue);
                    }

                    parameter.Set(doubleValue);
                    return true;

                case StorageType.ElementId:
                    throw new InvalidOperationException("ElementId storage is not supported.");

                default:
                    return false;
            }
        }

        private static bool TryParseInteger(string value, out int result)
        {
            string text = Normalize(value).ToLowerInvariant();
            if (text == "true" || text == "yes" || text == "y")
            {
                result = 1;
                return true;
            }

            if (text == "false" || text == "no" || text == "n")
            {
                result = 0;
                return true;
            }

            double doubleValue;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                result = Convert.ToInt32(doubleValue);
                return true;
            }

            return int.TryParse(text, out result);
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

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private class ParameterColumn
        {
            public ParameterColumn(int index, ParameterSelection selection)
            {
                Index = index;
                Selection = selection;
            }

            public int Index { get; private set; }
            public ParameterSelection Selection { get; private set; }
        }
    }
}
