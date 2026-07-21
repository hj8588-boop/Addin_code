using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitSheetBatchEditor.Engine
{
    public sealed class SheetBatchService
    {
        private readonly Document _document;

        public SheetBatchService(Document document) { _document = document; }

        public List<SheetRow> ReadSheets()
        {
            var titleBlocksBySheet = new FilteredElementCollector(_document)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .GroupBy(x => x.OwnerViewId.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            return new FilteredElementCollector(_document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(x => !x.IsPlaceholder)
                .OrderBy(x => x.SheetNumber, StringComparer.CurrentCultureIgnoreCase)
                .Select(sheet =>
                {
                    List<FamilyInstance> blocks;
                    titleBlocksBySheet.TryGetValue(sheet.Id.Value, out blocks);
                    FamilyInstance block = blocks == null ? null : blocks.FirstOrDefault();
                    string blockName = block == null
                        ? "None"
                        : block.Symbol.FamilyName + " : " + block.Symbol.Name;

                    return new SheetRow
                    {
                        SheetId = sheet.Id,
                        TitleBlockId = block == null ? ElementId.InvalidElementId : block.Id,
                        CurrentNumber = sheet.SheetNumber,
                        NewNumber = sheet.SheetNumber,
                        CurrentName = sheet.Name,
                        NewName = sheet.Name,
                        TitleBlockName = blockName,
                        Status = blocks == null || blocks.Count == 0 ? "No title block" : blocks.Count > 1 ? "Multiple title blocks (using first)" : "Ready"
                    };
                })
                .ToList();
        }

        public List<ParameterEditRow> ReadWritableParameters(IEnumerable<SheetRow> rows)
        {
            List<SheetRow> selected = rows.Where(x => x.IsSelected).ToList();
            List<Element> sheets = selected.Select(x => _document.GetElement(x.SheetId))
                .Where(x => x != null).ToList();
            List<Element> blocks = selected.Where(x => x.TitleBlockId != ElementId.InvalidElementId)
                .Select(x => _document.GetElement(x.TitleBlockId)).Where(x => x != null).ToList();
            Dictionary<long, HashSet<string>> instanceFamilyParameters = ReadFamilyParameterKeys(blocks, true);
            HashSet<string> sheetProjectParameters = ReadProjectParameterNames(BuiltInCategory.OST_Sheets);
            HashSet<string> titleBlockProjectParameters = ReadProjectParameterNames(BuiltInCategory.OST_TitleBlocks);

            var result = new List<ParameterEditRow>();
            AddWritableElementParameters(ParameterEditRow.SourceSheet, sheets,
                sheetProjectParameters, result);
            AddWritableFamilyParameters(ParameterEditRow.SourceTitleBlockInstance, blocks,
                instanceFamilyParameters, titleBlockProjectParameters, result);

            return result.OrderBy(x => x.Source).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private Dictionary<long, HashSet<string>> ReadFamilyParameterKeys(IEnumerable<Element> titleBlocks, bool instanceParameters)
        {
            var result = new Dictionary<long, HashSet<string>>();
            List<Family> families = titleBlocks.Cast<FamilyInstance>().Select(x => x.Symbol.Family)
                .GroupBy(x => x.Id.Value).Select(x => x.First()).ToList();

            foreach (Family family in families)
            {
                var keys = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                result[family.Id.Value] = keys;
                Document familyDocument = null;
                try
                {
                    familyDocument = _document.EditFamily(family);
                    foreach (FamilyParameter familyParameter in familyDocument.FamilyManager.Parameters)
                    {
                        if (familyParameter.Definition == null || familyParameter.IsInstance != instanceParameters) continue;
                        keys.Add(familyParameter.Definition.Name + "|" + familyParameter.StorageType);
                    }
                }
                catch
                {
                    // 편집할 수 없는 패밀리는 프로젝트 파라미터가 섞이는 것을 막기 위해 빈 목록으로 둡니다.
                }
                finally
                {
                    if (familyDocument != null && familyDocument.IsValidObject) familyDocument.Close(false);
                }
            }
            return result;
        }

        private HashSet<string> ReadProjectParameterNames(BuiltInCategory builtInCategory)
        {
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            Category targetCategory = _document.Settings.Categories.get_Item(builtInCategory);
            DefinitionBindingMapIterator iterator = _document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                InstanceBinding binding = iterator.Current as InstanceBinding;
                if (definition == null || binding == null) continue;

                foreach (Category category in binding.Categories)
                {
                    if (category.Id == targetCategory.Id)
                    {
                        names.Add(definition.Name);
                        break;
                    }
                }
            }
            return names;
        }

        private static void AddWritableElementParameters(string source, IEnumerable<Element> elements,
            ISet<string> allowedProjectParameterNames, List<ParameterEditRow> result)
        {
            List<Element> targetElements = elements.ToList();
            var parametersByKey = new Dictionary<string, List<Parameter>>(StringComparer.CurrentCultureIgnoreCase);

            foreach (Element element in targetElements)
            {
                foreach (Parameter parameter in element.Parameters)
                {
                    if (parameter.IsReadOnly || parameter.Definition == null
                        || parameter.StorageType == StorageType.None
                        || parameter.StorageType == StorageType.ElementId
                        || !allowedProjectParameterNames.Contains(parameter.Definition.Name)) continue;

                    string key = parameter.Definition.Name + "|" + parameter.StorageType;
                    List<Parameter> matches;
                    if (!parametersByKey.TryGetValue(key, out matches))
                    {
                        matches = new List<Parameter>();
                        parametersByKey.Add(key, matches);
                    }
                    matches.Add(parameter);
                }
            }

            foreach (KeyValuePair<string, List<Parameter>> pair in parametersByKey)
            {
                Parameter sample = pair.Value[0];
                List<string> values = pair.Value.Select(ReadParameterValue)
                    .Distinct(StringComparer.Ordinal).ToList();
                int missingCount = targetElements.Count - pair.Value.Count;
                bool hasDifferentValues = values.Count + (missingCount > 0 ? 1 : 0) > 1;
                string status = pair.Value.Count + "/" + targetElements.Count + " sheets";
                if (hasDifferentValues) status += " · Different values";
                if (missingCount > 0) status += " · Missing on " + missingCount;

                result.Add(new ParameterEditRow
                {
                    Source = source,
                    Name = sample.Definition.Name,
                    StorageType = sample.StorageType,
                    TypeName = GetStorageTypeName(sample),
                    CurrentValue = hasDifferentValues ? "<Multiple Values>" : values[0],
                    NewValue = hasDifferentValues ? string.Empty : values[0],
                    Status = status
                });
            }
        }

        private static void AddWritableFamilyParameters(string source, IEnumerable<Element> elements,
            IDictionary<long, HashSet<string>> allowedByFamily, ISet<string> allowedProjectParameterNames,
            List<ParameterEditRow> result)
        {
            List<Element> targetElements = elements.ToList();
            var parametersByKey = new Dictionary<string, List<Parameter>>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Element element in targetElements)
            {
                long familyId;
                FamilyInstance instance = element as FamilyInstance;
                FamilySymbol symbol = element as FamilySymbol;
                if (instance != null) familyId = instance.Symbol.Family.Id.Value;
                else if (symbol != null) familyId = symbol.Family.Id.Value;
                else continue;

                HashSet<string> allowedKeys;
                if (!allowedByFamily.TryGetValue(familyId, out allowedKeys)) continue;
                foreach (Parameter parameter in element.Parameters)
                {
                    if (parameter.IsReadOnly || parameter.Definition == null || parameter.StorageType == StorageType.None
                        || parameter.StorageType == StorageType.ElementId) continue;

                    string key = parameter.Definition.Name + "|" + parameter.StorageType;
                    bool isFamilyParameter = allowedKeys.Contains(key);
                    bool isTitleBlockProjectParameter = allowedProjectParameterNames != null
                        && allowedProjectParameterNames.Contains(parameter.Definition.Name);
                    if (!isFamilyParameter && !isTitleBlockProjectParameter) continue;
                    List<Parameter> matches;
                    if (!parametersByKey.TryGetValue(key, out matches))
                    {
                        matches = new List<Parameter>();
                        parametersByKey.Add(key, matches);
                    }
                    matches.Add(parameter);
                }
            }

            foreach (KeyValuePair<string, List<Parameter>> pair in parametersByKey)
            {
                Parameter sample = pair.Value[0];
                List<string> values = pair.Value.Select(ReadParameterValue)
                    .Distinct(StringComparer.Ordinal).ToList();
                int missingCount = targetElements.Count - pair.Value.Count;
                int distinctStateCount = values.Count + (missingCount > 0 ? 1 : 0);
                bool hasDifferentValues = distinctStateCount > 1;
                string status = pair.Value.Count + "/" + targetElements.Count + " title blocks";
                if (hasDifferentValues) status += " · " + distinctStateCount + " different states";
                if (missingCount > 0) status += " · Missing on " + missingCount;
                if (source == ParameterEditRow.SourceTitleBlockType) status += " · Affects all instances of this type";
                if (source == ParameterEditRow.SourceProjectInformation) status = "Affects the entire project";
                result.Add(new ParameterEditRow
                {
                    Source = source,
                    Name = sample.Definition.Name,
                    StorageType = sample.StorageType,
                    TypeName = GetStorageTypeName(sample),
                    CurrentValue = hasDifferentValues ? "<Multiple Values>" : values[0],
                    NewValue = hasDifferentValues ? string.Empty : values[0],
                    Status = status
                });
            }
        }

        private static string ReadParameterValue(Parameter parameter)
        {
            // Revit의 문자열 파라미터는 HasValue가 false여도 AsString()에 실제 값이
            // 반환되는 사례가 있으므로 문자열은 HasValue보다 먼저 읽습니다.
            if (parameter.StorageType == StorageType.String)
            {
                string text = parameter.AsString();
                if (text != null) return text;
                string displayedText = parameter.AsValueString();
                return displayedText ?? string.Empty;
            }
            if (!parameter.HasValue) return string.Empty;
            string formatted = parameter.AsValueString();
            if (!string.IsNullOrEmpty(formatted)) return formatted;
            if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger().ToString();
            if (parameter.StorageType == StorageType.Double) return parameter.AsDouble().ToString();
            return string.Empty;
        }

        private static string GetStorageTypeName(Parameter parameter)
        {
            if (parameter.StorageType == StorageType.String) return "Text";
            if (parameter.StorageType == StorageType.Integer) return "Integer/Yes-No";
            if (parameter.StorageType == StorageType.Double) return "Number/Length";
            return parameter.StorageType.ToString();
        }

        public List<SheetParameterValueRow> ReadSheetParameterValues(ParameterEditRow edit, IEnumerable<SheetRow> rows)
        {
            var result = new List<SheetParameterValueRow>();
            foreach (SheetRow row in rows.Where(x => x.IsSelected))
            {
                Element target = GetParameterTargets(edit.Source, new[] { row }).FirstOrDefault();
                Parameter parameter = target == null ? null : FindWritableParameter(target, edit);
                string currentValue = parameter == null ? string.Empty : ReadParameterValue(parameter);
                var valueRow = new SheetParameterValueRow
                {
                    SheetId = row.SheetId,
                    SheetNumber = row.CurrentNumber,
                    SheetName = row.CurrentName,
                    Source = edit.Source,
                    ParameterName = edit.Name,
                    StorageType = edit.StorageType,
                    CurrentValue = currentValue,
                    IsAvailable = parameter != null,
                    Status = parameter == null ? "Parameter not found" : "Editable"
                };
                valueRow.SetInitialValue(currentValue);
                result.Add(valueRow);
            }
            return result;
        }

        public ExcelTable BuildExcelTable(IEnumerable<SheetRow> rows)
        {
            List<SheetRow> selected = rows.Where(x => x.IsSelected).ToList();
            List<ParameterEditRow> parameterColumns = ReadWritableParameters(selected);
            var headers = new List<string> { "__SheetUniqueId", "Sheet Number", "Sheet Name", "Title Block Family", "Title Block Type" };
            headers.AddRange(parameterColumns.Select(x => x.Source + "|" + x.Name + "|" + x.StorageType));

            var dataRows = new List<List<string>>();
            foreach (SheetRow row in selected)
            {
                ViewSheet sheet = (ViewSheet)_document.GetElement(row.SheetId);
                FamilyInstance block = row.TitleBlockId == ElementId.InvalidElementId
                    ? null : _document.GetElement(row.TitleBlockId) as FamilyInstance;
                var values = new List<string>
                {
                    sheet.UniqueId, sheet.SheetNumber, sheet.Name,
                    block == null ? string.Empty : block.Symbol.FamilyName,
                    block == null ? string.Empty : block.Symbol.Name
                };

                foreach (ParameterEditRow column in parameterColumns)
                {
                    Element target = GetParameterTargets(column.Source, new[] { row }).FirstOrDefault();
                    Parameter parameter = target == null ? null : FindWritableParameter(target, column);
                    values.Add(parameter == null ? string.Empty : ReadParameterValue(parameter));
                }
                dataRows.Add(values);
            }
            return new ExcelTable { Headers = headers, Rows = dataRows };
        }

        public ExcelImportResult StageExcelImport(ExcelTable table, IReadOnlyCollection<SheetRow> rows)
        {
            int uniqueIdColumn = table.Headers.IndexOf("__SheetUniqueId");
            int numberColumn = FindHeader(table.Headers, "Sheet Number", "시트 번호");
            int nameColumn = FindHeader(table.Headers, "Sheet Name", "시트 이름");
            if (uniqueIdColumn < 0 && numberColumn < 0)
                throw new InvalidOperationException("Excel must contain a __SheetUniqueId or Sheet Number column.");

            var byUniqueId = rows.ToDictionary(x => _document.GetElement(x.SheetId).UniqueId, StringComparer.OrdinalIgnoreCase);
            var byNumber = rows.GroupBy(x => x.CurrentNumber, StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.CurrentCultureIgnoreCase);
            var result = new ExcelImportResult { Values = new List<ExcelImportValue>() };

            foreach (List<string> excelRow in table.Rows)
            {
                string uniqueId = Cell(excelRow, uniqueIdColumn);
                string sheetNumber = Cell(excelRow, numberColumn);
                SheetRow matched = null;
                if (!string.IsNullOrWhiteSpace(uniqueId)) byUniqueId.TryGetValue(uniqueId, out matched);
                if (matched == null && !string.IsNullOrWhiteSpace(sheetNumber)) byNumber.TryGetValue(sheetNumber, out matched);
                if (matched == null) { result.UnmatchedRows++; continue; }

                matched.IsSelected = true;
                if (numberColumn >= 0 && !string.IsNullOrWhiteSpace(sheetNumber)) matched.NewNumber = sheetNumber.Trim();
                string sheetName = Cell(excelRow, nameColumn);
                if (nameColumn >= 0 && !string.IsNullOrWhiteSpace(sheetName)) matched.NewName = sheetName.Trim();
                result.MatchedSheets++;

                for (int columnIndex = 0; columnIndex < table.Headers.Count; columnIndex++)
                {
                    string[] parts = table.Headers[columnIndex].Split('|');
                    if (parts.Length != 3) continue;
                    string source = NormalizeSource(parts[0]);
                    if (!IsKnownSource(source)) continue;
                    StorageType storageType;
                    if (!Enum.TryParse(parts[2], out storageType)) continue;
                    result.Values.Add(new ExcelImportValue
                    {
                        SheetId = matched.SheetId,
                        Source = source, Name = parts[1], StorageType = storageType,
                        Value = Cell(excelRow, columnIndex)
                    });
                    result.MappedValues++;
                }
            }
            ValidateExcelConflicts(result.Values, rows);
            return result;
        }

        private static string Cell(IList<string> row, int index)
        {
            return index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;
        }

        private static int FindHeader(IList<string> headers, string englishName, string legacyKoreanName)
        {
            int index = headers.IndexOf(englishName);
            return index >= 0 ? index : headers.IndexOf(legacyKoreanName);
        }

        private static string NormalizeSource(string source)
        {
            if (source == "시트") return ParameterEditRow.SourceSheet;
            if (source == "도곽 인스턴스") return ParameterEditRow.SourceTitleBlockInstance;
            if (source == "도곽 타입") return ParameterEditRow.SourceTitleBlockType;
            if (source == "프로젝트 정보") return ParameterEditRow.SourceProjectInformation;
            return source;
        }

        private static bool IsKnownSource(string source)
        {
            return source == ParameterEditRow.SourceSheet
                || source == ParameterEditRow.SourceTitleBlockInstance
                || source == ParameterEditRow.SourceTitleBlockType
                || source == ParameterEditRow.SourceProjectInformation;
        }

        private void ValidateExcelConflicts(IEnumerable<ExcelImportValue> values, IReadOnlyCollection<SheetRow> rows)
        {
            var sheetRows = rows.ToDictionary(x => x.SheetId.Value);
            var assigned = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (ExcelImportValue value in values)
            {
                SheetRow row;
                if (!sheetRows.TryGetValue(value.SheetId.Value, out row)) continue;
                Element target = GetParameterTargets(value.Source, new[] { row }).FirstOrDefault();
                if (target == null) continue;
                string key = target.Id.Value + "|" + value.Source + "|" + value.Name + "|" + value.StorageType;
                string previous;
                if (assigned.TryGetValue(key, out previous) && !string.Equals(previous, value.Value, StringComparison.CurrentCulture))
                    throw new InvalidOperationException("Excel contains conflicting values for " + value.Source
                        + " parameter '" + value.Name + "'. Enter only one value for the same target.");
                assigned[key] = value.Value;
            }
        }

        public string Validate(IReadOnlyCollection<SheetRow> rows)
        {
            List<SheetRow> selected = rows.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) return "Select at least one sheet to edit.";
            if (selected.Any(x => string.IsNullOrWhiteSpace(x.NewNumber))) return "Sheet numbers cannot be blank.";

            var finalNumbers = new List<string>();
            foreach (ViewSheet sheet in new FilteredElementCollector(_document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                SheetRow edited = selected.FirstOrDefault(x => x.SheetId == sheet.Id);
                finalNumbers.Add((edited == null ? sheet.SheetNumber : edited.NewNumber).Trim());
            }

            string duplicate = finalNumbers
                .GroupBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            return duplicate == null ? null : "Duplicate final sheet number: " + duplicate;
        }

        public string ValidateParameters(IEnumerable<ParameterEditRow> edits)
        {
            foreach (ParameterEditRow edit in edits.Where(x => x.IsSelected))
            {
                if (edit.NewValue == null) return edit.Name + ": no new value was entered.";
                int integerValue;
                if (edit.StorageType == StorageType.Integer && !TryParseInteger(edit.NewValue, out integerValue))
                    return edit.Name + ": enter an integer or Yes/No value.";
            }
            return null;
        }

        public void Apply(IReadOnlyCollection<SheetRow> rows, IReadOnlyCollection<ParameterEditRow> parameterEdits,
            IReadOnlyCollection<ExcelImportValue> excelValues)
        {
            List<SheetRow> selected = rows.Where(x => x.IsSelected).ToList();
            string validation = Validate(rows);
            if (validation != null) throw new InvalidOperationException(validation);
            validation = ValidateParameters(parameterEdits);
            if (validation != null) throw new InvalidOperationException(validation);
            ValidateExcelConflicts(excelValues, rows);

            using (var group = new TransactionGroup(_document, "Sheet Batch Editor"))
            {
                group.Start();
                try
                {
                    List<SheetRow> numberChanges = selected
                        .Where(x => !string.Equals(x.CurrentNumber, x.NewNumber == null ? null : x.NewNumber.Trim(), StringComparison.CurrentCulture))
                        .ToList();

                    if (numberChanges.Count > 0)
                    {
                        using (var transaction = new Transaction(_document, "Apply Temporary Sheet Numbers"))
                        {
                            transaction.Start();
                            foreach (SheetRow row in numberChanges)
                                ((ViewSheet)_document.GetElement(row.SheetId)).SheetNumber = "__SBE_" + Guid.NewGuid().ToString("N");
                            transaction.Commit();
                        }
                    }

                    using (var transaction = new Transaction(_document, "Apply Sheet and Title Block Values"))
                    {
                        transaction.Start();
                        foreach (SheetRow row in selected)
                        {
                            var sheet = (ViewSheet)_document.GetElement(row.SheetId);
                            sheet.SheetNumber = row.NewNumber.Trim();
                            if (!string.IsNullOrWhiteSpace(row.NewName)) sheet.Name = row.NewName.Trim();

                        }

                        foreach (ParameterEditRow edit in parameterEdits.Where(x => x.IsSelected))
                        {
                            foreach (Element target in GetParameterTargets(edit.Source, selected))
                            {
                                Parameter parameter = FindWritableParameter(target, edit);
                                if (parameter != null) SetParameterValue(parameter, edit.NewValue);
                            }
                        }

                        foreach (ExcelImportValue imported in excelValues)
                        {
                            SheetRow importedRow = selected.FirstOrDefault(x => x.SheetId == imported.SheetId);
                            if (importedRow == null) continue;
                            var lookup = new ParameterEditRow
                            {
                                Source = imported.Source, Name = imported.Name, StorageType = imported.StorageType
                            };
                            foreach (Element target in GetParameterTargets(imported.Source, new[] { importedRow }))
                            {
                                Parameter parameter = FindWritableParameter(target, lookup);
                                if (parameter != null) SetParameterValue(parameter, imported.Value);
                            }
                        }
                        transaction.Commit();
                    }

                    group.Assimilate();
                }
                catch
                {
                    group.RollBack();
                    throw;
                }
            }
        }

        private IEnumerable<Element> GetParameterTargets(string source, IEnumerable<SheetRow> selectedRows)
        {
            List<SheetRow> rows = selectedRows.ToList();
            if (source == ParameterEditRow.SourceSheet)
                return rows.Select(x => _document.GetElement(x.SheetId)).Where(x => x != null);

            List<Element> blocks = rows.Where(x => x.TitleBlockId != ElementId.InvalidElementId)
                .Select(x => _document.GetElement(x.TitleBlockId)).Where(x => x != null).ToList();
            if (source == ParameterEditRow.SourceTitleBlockInstance) return blocks;
            if (source == ParameterEditRow.SourceTitleBlockType)
                return blocks.Select(x => _document.GetElement(x.GetTypeId())).Where(x => x != null)
                    .GroupBy(x => x.Id.Value).Select(x => x.First());
            if (source == ParameterEditRow.SourceProjectInformation)
                return new[] { (Element)_document.ProjectInformation };
            return Enumerable.Empty<Element>();
        }

        private static Parameter FindWritableParameter(Element element, ParameterEditRow edit)
        {
            List<Parameter> candidates = element.Parameters.Cast<Parameter>().Where(parameter =>
                parameter.Definition != null
                && string.Equals(parameter.Definition.Name, edit.Name, StringComparison.CurrentCultureIgnoreCase)
                && parameter.StorageType == edit.StorageType && !parameter.IsReadOnly).ToList();

            // 같은 이름의 패밀리/프로젝트 파라미터가 중복될 때 빈 항목이 먼저
            // 반환될 수 있으므로 실제 값이 들어 있는 파라미터를 우선 사용합니다.
            return candidates.FirstOrDefault(parameter => !string.IsNullOrEmpty(ReadParameterValue(parameter)))
                ?? candidates.FirstOrDefault();
        }

        private static void SetParameterValue(Parameter parameter, string value)
        {
            if (parameter.StorageType == StorageType.String) { parameter.Set(value ?? string.Empty); return; }
            if (parameter.StorageType == StorageType.Integer)
            {
                int integerValue;
                if (!TryParseInteger(value, out integerValue))
                    throw new InvalidOperationException(parameter.Definition.Name + ": cannot convert the value to an integer.");
                parameter.Set(integerValue);
                return;
            }
            if (parameter.StorageType == StorageType.Double && !parameter.SetValueString(value))
                throw new InvalidOperationException(parameter.Definition.Name + ": the value does not match the Revit unit format.");
        }

        private static bool TryParseInteger(string value, out int result)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "예" || normalized == "yes" || normalized == "true") { result = 1; return true; }
            if (normalized == "아니오" || normalized == "아니요" || normalized == "no" || normalized == "false") { result = 0; return true; }
            return int.TryParse(normalized, out result);
        }
    }
}
