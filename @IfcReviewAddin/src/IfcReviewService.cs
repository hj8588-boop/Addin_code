using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace IfcReviewAddin
{
    public class IfcReviewService
    {
        public List<CategoryOption> GetDefaultCategories()
        {
            return new List<CategoryOption>
            {
                CreateCategoryOption(BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment"),
                CreateCategoryOption(BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures"),
                CreateCategoryOption(BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures"),
                CreateCategoryOption(BuiltInCategory.OST_LightingDevices, "Lighting Devices"),
                CreateCategoryOption(BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Devices"),
                CreateCategoryOption(BuiltInCategory.OST_CommunicationDevices, "Communication Devices"),
                CreateCategoryOption(BuiltInCategory.OST_DataDevices, "Data Devices"),
                CreateCategoryOption(BuiltInCategory.OST_SecurityDevices, "Security Devices"),
                CreateCategoryOption(BuiltInCategory.OST_TelephoneDevices, "Telephone Devices"),
                CreateCategoryOption(BuiltInCategory.OST_NurseCallDevices, "Nurse Call Devices"),
                CreateCategoryOption(BuiltInCategory.OST_CableTray, "Cable Trays"),
                CreateCategoryOption(BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings"),
                CreateCategoryOption(BuiltInCategory.OST_Conduit, "Conduits"),
                CreateCategoryOption(BuiltInCategory.OST_ConduitFitting, "Conduit Fittings")
            };
        }

        public List<CategoryOption> GetSelectableCategories(Document doc)
        {
            var defaultIds = new HashSet<int>(GetDefaultCategories().Select(c => c.BuiltInCategoryId));
            var counts = new Dictionary<int, CategoryOption>();

            foreach (FamilyInstance inst in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance)))
            {
                Category category = inst.Category;
                if (category == null) continue;

                int id = category.Id.IntegerValue;
                CategoryOption option;
                if (!counts.TryGetValue(id, out option))
                {
                    option = new CategoryOption
                    {
                        BuiltInCategoryId = id,
                        Name = category.Name,
                        ElementCount = 0,
                        CheckedByDefault = defaultIds.Contains(id)
                    };
                    counts[id] = option;
                }
                option.ElementCount++;
            }

            foreach (CategoryOption option in GetDefaultCategories())
            {
                if (!counts.ContainsKey(option.BuiltInCategoryId))
                    counts[option.BuiltInCategoryId] = option;
            }

            return counts.Values
                .OrderByDescending(c => c.CheckedByDefault)
                .ThenBy(c => c.Name)
                .ToList();
        }

        public List<IfcReviewRow> CollectRows(Document doc, IEnumerable<int> categoryIds, bool includeParameters)
        {
            var selected = new HashSet<int>(categoryIds);
            var rows = new List<IfcReviewRow>();

            foreach (FamilyInstance inst in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance)))
            {
                if (!IsSelectedCategory(inst.Category, selected)) continue;

                FamilySymbol symbol = inst.Symbol;
                Family family = symbol != null ? symbol.Family : null;
                Level level = doc.GetElement(inst.LevelId) as Level;
                string ifcClass = GetIfcClassText(inst);
                string ifcExportAs = GetParameterText(inst, "IfcExportAs", "IFCExportAs");
                string ifcExportType = GetParameterText(inst, "IfcExportType", "IFCExportType");

                if (string.IsNullOrWhiteSpace(ifcClass) && symbol != null)
                    ifcClass = GetIfcClassText(symbol);
                if (string.IsNullOrWhiteSpace(ifcExportAs) && symbol != null)
                    ifcExportAs = GetParameterText(symbol, "IfcExportAs", "IFCExportAs");
                if (string.IsNullOrWhiteSpace(ifcExportType) && symbol != null)
                    ifcExportType = GetParameterText(symbol, "IfcExportType", "IFCExportType");
                if (string.IsNullOrWhiteSpace(ifcExportAs))
                    ifcExportAs = ifcClass;

                var row = new IfcReviewRow
                {
                    ElementId = inst.Id.IntegerValue,
                    UniqueId = inst.UniqueId,
                    RevitUniqueId = inst.UniqueId,
                    IfcGlobalId = GetIfcGlobalId(doc, inst.Id),
                    Category = inst.Category != null ? inst.Category.Name : "",
                    Family = family != null ? family.Name : "",
                    Type = symbol != null ? symbol.Name : "",
                    TypeId = symbol != null ? symbol.Id.IntegerValue : -1,
                    Level = level != null ? level.Name : "",
                    IfcClass = ifcClass,
                    IfcExportAs = ifcExportAs,
                    IfcExportType = ifcExportType,
                    MissingIfcClass = string.IsNullOrWhiteSpace(ifcClass),
                    MissingIfcExportAs = string.IsNullOrWhiteSpace(ifcExportAs),
                    MissingIfcExportType = string.IsNullOrWhiteSpace(ifcExportType),
                    InstanceParameters = includeParameters ? GetParameterMap(inst) : new Dictionary<string, string>(),
                    TypeParameters = includeParameters && symbol != null ? GetParameterMap(symbol) : new Dictionary<string, string>()
                };

                rows.Add(row);
            }

            return rows
                .OrderBy(r => r.Category)
                .ThenBy(r => r.Family)
                .ThenBy(r => r.Type)
                .ThenBy(r => r.ElementId)
                .ToList();
        }

        public ReportSaveResult SaveReport(Document doc, string outputFolder, IList<IfcReviewRow> rows)
        {
            EnsureFolder(outputFolder);
            string baseName = GetModelBaseName(doc);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csvPath = Path.Combine(outputFolder, baseName + "_ifc_review_" + stamp + ".csv");
            string jsonPath = Path.Combine(outputFolder, baseName + "_ifc_review_" + stamp + ".json");

            WriteCsv(csvPath, rows);
            WriteJson(jsonPath, new
            {
                source_model = doc.PathName,
                exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                object_count = rows.Count,
                missing_ifc_class = rows.Count(r => r.MissingIfcClass),
                missing_ifc_export_as = rows.Count(r => r.MissingIfcExportAs),
                missing_ifc_export_type = rows.Count(r => r.MissingIfcExportType),
                objects = rows
            });

            return new ReportSaveResult { CsvPath = csvPath, JsonPath = jsonPath };
        }

        public IfcExportResult ExportIfcWithReport(Document doc, string outputFolder, string fileName, IEnumerable<int> categoryIds, bool includeParameters)
        {
            EnsureFolder(outputFolder);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = GetModelBaseName(doc) + "_electrical.ifc";
            if (!fileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
                fileName += ".ifc";

            int writtenUniqueIds = TryWriteRevitUniqueIdValues(doc, categoryIds);
            List<IfcReviewRow> rows = CollectRows(doc, categoryIds, includeParameters);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string csvPath = Path.Combine(outputFolder, baseName + "_export_list.csv");
            string jsonPath = Path.Combine(outputFolder, baseName + "_export_list.json");
            WriteCsv(csvPath, rows);
            WriteJson(jsonPath, new
            {
                source_model = doc.PathName,
                ifc_file = Path.Combine(outputFolder, fileName),
                exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                object_count = rows.Count,
                revit_unique_id_values_written = writtenUniqueIds,
                objects = rows
            });

            ElementId exportViewId;
            var selected = new HashSet<int>(categoryIds);
            using (Transaction tx = new Transaction(doc, "IFC Review Create Export View"))
            {
                tx.Start();
                ViewFamilyType viewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (viewType == null)
                    throw new InvalidOperationException("No 3D ViewFamilyType was found.");

                View3D view = View3D.CreateIsometric(doc, viewType.Id);
                view.Name = "IFC_Review_Export_" + DateTime.Now.ToString("HHmmss");
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    if (!cat.get_AllowsVisibilityControl(view)) continue;
                    try { view.SetCategoryHidden(cat.Id, !IsSelectedCategory(cat, selected)); }
                    catch { }
                }
                exportViewId = view.Id;
                tx.Commit();
            }

            bool exported;
            string ifcPath = Path.Combine(outputFolder, fileName);
            try
            {
                var options = new IFCExportOptions();
                options.FilterViewId = exportViewId;
                options.FileVersion = IFCVersion.IFC4;
                options.AddOption("VisibleElementsOfCurrentView", "true");
                options.AddOption("ExportBaseQuantities", "true");
                options.AddOption("ExportInternalRevitPropertySets", "true");
                exported = doc.Export(outputFolder, fileName, options);
            }
            finally
            {
                using (Transaction tx = new Transaction(doc, "IFC Review Remove Export View"))
                {
                    tx.Start();
                    doc.Delete(exportViewId);
                    tx.Commit();
                }
            }

            IfcFileReviewResult fileReview = File.Exists(ifcPath)
                ? AnalyzeIfcFile(ifcPath, null)
                : new IfcFileReviewResult
                {
                    IfcPath = ifcPath,
                    TotalEntityCount = 0,
                    ObjectCount = 0,
                    ProxyCount = 0,
                    ClassCounts = new List<IfcClassCountRow>(),
                    Objects = new List<IfcFileObjectRow>()
                };

            List<IfcMissingCheckRow> missingRows = BuildMissingCheckRows(rows, fileReview);
            string missingCsvPath = Path.Combine(outputFolder, baseName + "_missing_check.csv");
            string missingJsonPath = Path.Combine(outputFolder, baseName + "_missing_check.json");
            WriteMissingCheckCsv(missingCsvPath, missingRows);
            WriteJson(missingJsonPath, new
            {
                source_model = doc.PathName,
                ifc_file = ifcPath,
                checked_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                revit_export_target_count = rows.Count,
                revit_unique_id_values_written = writtenUniqueIds,
                ifc_object_count = fileReview.ObjectCount,
                suspected_missing_count = Math.Max(0, rows.Count - fileReview.ObjectCount),
                note = "This is a count-based check. Exact one-to-one matching requires stable element identifiers in IFC property sets.",
                rows = missingRows
            });

            return new IfcExportResult
            {
                Success = exported,
                IfcPath = ifcPath,
                CsvPath = csvPath,
                JsonPath = jsonPath,
                MissingCheckCsvPath = missingCsvPath,
                MissingCheckJsonPath = missingJsonPath,
                ObjectCount = rows.Count,
                RevitUniqueIdValuesWritten = writtenUniqueIds,
                ExportedIfcObjectCount = fileReview.ObjectCount,
                SuspectedMissingCount = Math.Max(0, rows.Count - fileReview.ObjectCount)
            };
        }

        public IfcFileReviewResult ReviewExistingIfcFile(string ifcPath, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
                throw new FileNotFoundException("IFC file was not found.", ifcPath);

            EnsureFolder(outputFolder);
            string baseName = Path.GetFileNameWithoutExtension(ifcPath);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csvPath = Path.Combine(outputFolder, baseName + "_ifc_file_review_" + stamp + ".csv");
            string jsonPath = Path.Combine(outputFolder, baseName + "_ifc_file_review_" + stamp + ".json");

            IfcFileReviewResult result = AnalyzeIfcFile(ifcPath, outputFolder);
            result.CsvPath = csvPath;
            result.JsonPath = jsonPath;
            WriteIfcFileObjectCsv(csvPath, result.Objects);
            WriteJson(jsonPath, result);

            return result;
        }

        public IfcModelCompareResult CompareCurrentModelWithIfc(Document doc, string ifcPath, string outputFolder, IEnumerable<int> categoryIds)
        {
            if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
                throw new FileNotFoundException("IFC file was not found.", ifcPath);

            EnsureFolder(outputFolder);

            List<IfcReviewRow> revitRows = CollectRows(doc, categoryIds, false);
            IfcFileReviewResult ifcReview = AnalyzeIfcFile(ifcPath, null);
            List<IfcModelCompareRow> compareRows = BuildModelCompareRows(revitRows, ifcReview);
            bool usesRevitUniqueId = ifcReview.Objects.Any(o => !string.IsNullOrWhiteSpace(o.RevitUniqueId));
            bool usesIfcGlobalId = !usesRevitUniqueId &&
                ifcReview.Objects.Any(o => !string.IsNullOrWhiteSpace(o.GlobalId)) &&
                revitRows.Any(r => !string.IsNullOrWhiteSpace(r.IfcGlobalId));

            string baseName = Path.GetFileNameWithoutExtension(ifcPath);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csvPath = Path.Combine(outputFolder, baseName + "_model_ifc_compare_" + stamp + ".csv");
            string jsonPath = Path.Combine(outputFolder, baseName + "_model_ifc_compare_" + stamp + ".json");

            WriteModelCompareCsv(csvPath, compareRows);

            var result = new IfcModelCompareResult
            {
                IfcPath = ifcPath,
                CsvPath = csvPath,
                JsonPath = jsonPath,
                RevitObjectCount = revitRows.Count,
                IfcObjectCount = ifcReview.ObjectCount,
                DifferenceCount = compareRows.Sum(r => Math.Abs(r.Difference)),
                Rows = compareRows
            };

            WriteJson(jsonPath, new
            {
                source_model = doc.PathName,
                ifc_file = ifcPath,
                checked_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                revit_object_count = result.RevitObjectCount,
                ifc_object_count = result.IfcObjectCount,
                difference_count = result.DifferenceCount,
                note = usesRevitUniqueId
                    ? "This compares by exact RevitUniqueId values found in IFC property sets."
                    : (usesIfcGlobalId
                        ? "This compares by exact IFC GlobalId values generated by Revit's default IFC export id."
                        : "This IFC file has no usable RevitUniqueId or IFC GlobalId values, so comparison falls back to family/type/class text matching."),
                rows = compareRows
            });

            return result;
        }

        private static IfcFileReviewResult AnalyzeIfcFile(string ifcPath, string outputFolder)
        {
            var classCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var objects = new List<IfcFileObjectRow>();
            var objectsByStepId = new Dictionary<int, IfcFileObjectRow>();
            var propertyValues = new Dictionary<int, IfcPropertyValue>();
            var propertySetMembers = new Dictionary<int, List<int>>();
            var objectPropertySets = new Dictionary<int, List<int>>();
            int totalEntities = 0;

            foreach (string statement in ReadIfcStatements(ifcPath))
            {
                int stepId;
                string ifcClass;
                string argsText;
                if (!TryParseIfcEntity(statement, out stepId, out ifcClass, out argsText))
                    continue;

                totalEntities++;
                if (!classCounts.ContainsKey(ifcClass))
                    classCounts[ifcClass] = 0;
                classCounts[ifcClass]++;

                List<string> args = SplitStepArguments(argsText);
                if (string.Equals(ifcClass, "IFCPROPERTYSINGLEVALUE", StringComparison.OrdinalIgnoreCase))
                {
                    string propertyName = args.Count > 0 ? ExtractStepString(args[0]) : "";
                    string propertyValue = args.Count > 2 ? ExtractIfcSimpleValue(args[2]) : "";
                    if (!string.IsNullOrWhiteSpace(propertyName) && !propertyValues.ContainsKey(stepId))
                        propertyValues[stepId] = new IfcPropertyValue { Name = propertyName, Value = propertyValue };
                    continue;
                }

                if (string.Equals(ifcClass, "IFCPROPERTYSET", StringComparison.OrdinalIgnoreCase))
                {
                    propertySetMembers[stepId] = args.Count > 4 ? ExtractStepReferences(args[4]) : new List<int>();
                    continue;
                }

                if (string.Equals(ifcClass, "IFCRELDEFINESBYPROPERTIES", StringComparison.OrdinalIgnoreCase))
                {
                    List<int> relatedObjects = args.Count > 4 ? ExtractStepReferences(args[4]) : new List<int>();
                    List<int> relatingPropertySets = args.Count > 5 ? ExtractStepReferences(args[5]) : new List<int>();
                    foreach (int objectId in relatedObjects)
                    {
                        List<int> sets;
                        if (!objectPropertySets.TryGetValue(objectId, out sets))
                        {
                            sets = new List<int>();
                            objectPropertySets[objectId] = sets;
                        }
                        sets.AddRange(relatingPropertySets);
                    }
                    continue;
                }

                string globalId = args.Count > 0 ? ExtractStepString(args[0]) : "";
                if (!LooksLikeExportedObject(ifcClass, globalId))
                    continue;

                string name = args.Count > 2 ? ExtractStepString(args[2]) : "";
                string description = args.Count > 3 ? ExtractStepString(args[3]) : "";
                string objectType = args.Count > 4 ? ExtractStepString(args[4]) : "";
                string predefinedType = FindPredefinedType(args);
                string status = string.Equals(ifcClass, "IFCBUILDINGELEMENTPROXY", StringComparison.OrdinalIgnoreCase)
                    ? "Proxy review needed"
                    : "OK";

                objects.Add(new IfcFileObjectRow
                {
                    StepId = stepId,
                    IfcClass = ifcClass,
                    GlobalId = globalId,
                    Name = name,
                    Description = description,
                    ObjectType = objectType,
                    PredefinedType = predefinedType,
                    Status = status
                });
                objectsByStepId[stepId] = objects[objects.Count - 1];
            }

            foreach (KeyValuePair<int, IfcFileObjectRow> objectPair in objectsByStepId)
            {
                List<int> setIds;
                if (!objectPropertySets.TryGetValue(objectPair.Key, out setIds))
                    continue;

                Dictionary<string, string> props = BuildPropertyMap(setIds, propertySetMembers, propertyValues);
                IfcFileObjectRow row = objectPair.Value;
                row.RevitUniqueId = GetFirstProperty(props, "RevitUniqueId", "Revit UniqueId", "UniqueId", "Element UniqueId");
                row.RevitElementId = GetFirstProperty(props, "ElementId", "Element Id", "Revit Element Id", "Id");
                row.RevitCategory = GetFirstProperty(props, "Category", "Revit Category");
                row.RevitFamily = GetFirstProperty(props, "Family", "Family Name");
                row.RevitType = GetFirstProperty(props, "Type", "Type Name", "Family and Type");
            }

            List<IfcClassCountRow> classCountRows = classCounts
                .Select(kvp => new IfcClassCountRow { IfcClass = kvp.Key, Count = kvp.Value })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.IfcClass)
                .ToList();

            return new IfcFileReviewResult
            {
                IfcPath = ifcPath,
                CsvPath = "",
                JsonPath = "",
                TotalEntityCount = totalEntities,
                ObjectCount = objects.Count,
                ProxyCount = objects.Count(o => string.Equals(o.IfcClass, "IFCBUILDINGELEMENTPROXY", StringComparison.OrdinalIgnoreCase)),
                ClassCounts = classCountRows,
                Objects = objects
            };
        }

        private static Dictionary<string, string> BuildPropertyMap(
            IEnumerable<int> propertySetIds,
            Dictionary<int, List<int>> propertySetMembers,
            Dictionary<int, IfcPropertyValue> propertyValues)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (int propertySetId in propertySetIds)
            {
                List<int> propertyIds;
                if (!propertySetMembers.TryGetValue(propertySetId, out propertyIds))
                    continue;

                foreach (int propertyId in propertyIds)
                {
                    IfcPropertyValue property;
                    if (!propertyValues.TryGetValue(propertyId, out property))
                        continue;
                    if (string.IsNullOrWhiteSpace(property.Name) || values.ContainsKey(property.Name))
                        continue;

                    values[property.Name] = property.Value;
                }
            }
            return values;
        }

        private static string GetFirstProperty(Dictionary<string, string> props, params string[] names)
        {
            foreach (string name in names)
            {
                string value;
                if (props.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static List<IfcMissingCheckRow> BuildMissingCheckRows(IList<IfcReviewRow> revitRows, IfcFileReviewResult fileReview)
        {
            var ifcCounts = fileReview.Objects
                .GroupBy(o => NormalizeIfcClass(o.IfcClass))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            return revitRows
                .GroupBy(r => new
                {
                    r.Category,
                    r.Family,
                    r.Type,
                    ExpectedIfcClass = NormalizeIfcClass(!string.IsNullOrWhiteSpace(r.IfcClass) ? r.IfcClass : r.IfcExportAs)
                })
                .Select(g =>
                {
                    int ifcCount = ifcCounts.ContainsKey(g.Key.ExpectedIfcClass) ? ifcCounts[g.Key.ExpectedIfcClass] : 0;
                    int missing = Math.Max(0, g.Count() - ifcCount);
                    return new IfcMissingCheckRow
                    {
                        Category = g.Key.Category,
                        Family = g.Key.Family,
                        Type = g.Key.Type,
                        ExpectedIfcClass = g.Key.ExpectedIfcClass,
                        RevitCount = g.Count(),
                        IfcClassCount = ifcCount,
                        SuspectedMissingCount = missing,
                        Status = missing > 0 ? "Review needed" : "OK"
                    };
                })
                .OrderByDescending(r => r.SuspectedMissingCount)
                .ThenBy(r => r.Category)
                .ThenBy(r => r.Family)
                .ThenBy(r => r.Type)
                .ToList();
        }

        private static List<IfcModelCompareRow> BuildModelCompareRows(IList<IfcReviewRow> revitRows, IfcFileReviewResult fileReview)
        {
            bool useRevitUniqueIdOnly = fileReview.Objects.Any(o => !string.IsNullOrWhiteSpace(o.RevitUniqueId));
            bool useIfcGlobalIdOnly = !useRevitUniqueIdOnly &&
                fileReview.Objects.Any(o => !string.IsNullOrWhiteSpace(o.GlobalId)) &&
                revitRows.Any(r => !string.IsNullOrWhiteSpace(r.IfcGlobalId));

            return revitRows
                .GroupBy(r => new
                {
                    r.Category,
                    r.Family,
                    r.Type,
                    ExpectedIfcClass = NormalizeIfcClass(!string.IsNullOrWhiteSpace(r.IfcClass) ? r.IfcClass : r.IfcExportAs)
                })
                .Select(g =>
                {
                    List<IfcReviewRow> groupRows = g
                        .OrderBy(r => r.ElementId)
                        .ToList();
                    HashSet<string> revitUniqueIds = new HashSet<string>(
                        groupRows.Select(r => r.RevitUniqueId).Where(v => !string.IsNullOrWhiteSpace(v)),
                        StringComparer.OrdinalIgnoreCase);
                    HashSet<string> revitIfcGlobalIds = new HashSet<string>(
                        groupRows.Select(r => r.IfcGlobalId).Where(v => !string.IsNullOrWhiteSpace(v)),
                        StringComparer.OrdinalIgnoreCase);

                    List<IfcFileObjectRow> sameClass = fileReview.Objects
                        .Where(o => string.Equals(NormalizeIfcClass(o.IfcClass), g.Key.ExpectedIfcClass, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    int uniqueIdMatches = fileReview.Objects.Count(o =>
                        !string.IsNullOrWhiteSpace(o.RevitUniqueId) && revitUniqueIds.Contains(o.RevitUniqueId));
                    int globalIdMatches = fileReview.Objects.Count(o =>
                        !string.IsNullOrWhiteSpace(o.GlobalId) && revitIfcGlobalIds.Contains(o.GlobalId));
                    int propertyMatches = fileReview.Objects.Count(o => IfcRevitPropertiesMatch(o, g.Key.Category, g.Key.Family, g.Key.Type));
                    int typeMatches = sameClass.Count(o => IfcTextMatches(o, g.Key.Type));
                    int familyMatches = sameClass.Count(o => IfcTextMatches(o, g.Key.Family));
                    int anyTextMatches = fileReview.Objects.Count(o => IfcTextMatches(o, g.Key.Type) || IfcTextMatches(o, g.Key.Family));

                    int matched = useIfcGlobalIdOnly ? globalIdMatches : uniqueIdMatches;
                    string method = useRevitUniqueIdOnly
                        ? "RevitUniqueId exact"
                        : (useIfcGlobalIdOnly ? "IFC GlobalId exact" : "RevitUniqueId");
                    if (!useRevitUniqueIdOnly && !useIfcGlobalIdOnly && matched == 0)
                    {
                        matched = propertyMatches;
                        method = "IFC Revit properties";
                    }
                    if (!useRevitUniqueIdOnly && !useIfcGlobalIdOnly && matched == 0)
                    {
                        matched = typeMatches;
                        method = "IFC class + type text";
                    }
                    if (!useRevitUniqueIdOnly && !useIfcGlobalIdOnly && matched == 0)
                    {
                        matched = familyMatches;
                        method = "IFC class + family text";
                    }
                    if (!useRevitUniqueIdOnly && !useIfcGlobalIdOnly && matched == 0 && !string.Equals(g.Key.ExpectedIfcClass, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                    {
                        matched = sameClass.Count;
                        method = "IFC class only";
                    }
                    if (!useRevitUniqueIdOnly && !useIfcGlobalIdOnly && matched == 0)
                    {
                        matched = anyTextMatches;
                        method = matched > 0 ? "IFC text only" : "No match";
                    }
                    if (useRevitUniqueIdOnly && matched == 0)
                        method = "RevitUniqueId exact - no match";
                    if (useIfcGlobalIdOnly && matched == 0)
                        method = "IFC GlobalId exact - no match";

                    int diff = g.Count() - matched;
                    string revitElementIds = JoinElementIds(groupRows);
                    string differenceElementIds = diff > 0
                        ? JoinElementIds(GetDifferenceCandidateRows(groupRows, fileReview.Objects, diff, useRevitUniqueIdOnly, useIfcGlobalIdOnly))
                        : "";

                    return new IfcModelCompareRow
                    {
                        Category = g.Key.Category,
                        Family = g.Key.Family,
                        Type = g.Key.Type,
                        ExpectedIfcClass = g.Key.ExpectedIfcClass,
                        RevitCount = g.Count(),
                        IfcMatchedCount = matched,
                        Difference = diff,
                        RevitElementIds = revitElementIds,
                        DifferenceElementIds = differenceElementIds,
                        MatchMethod = method,
                        Status = diff == 0 ? "OK" : "Review needed"
                    };
                })
                .OrderByDescending(r => Math.Abs(r.Difference))
                .ThenBy(r => r.Category)
                .ThenBy(r => r.Family)
                .ThenBy(r => r.Type)
                .ToList();
        }

        private static List<IfcReviewRow> GetDifferenceCandidateRows(
            List<IfcReviewRow> revitRows,
            IList<IfcFileObjectRow> ifcRows,
            int differenceCount,
            bool useRevitUniqueIdOnly,
            bool useIfcGlobalIdOnly)
        {
            if (differenceCount <= 0)
                return new List<IfcReviewRow>();

            if (useIfcGlobalIdOnly)
            {
                HashSet<string> matchedGlobalIds = new HashSet<string>(
                    ifcRows.Select(r => r.GlobalId).Where(v => !string.IsNullOrWhiteSpace(v)),
                    StringComparer.OrdinalIgnoreCase);

                return revitRows
                    .Where(r => string.IsNullOrWhiteSpace(r.IfcGlobalId) || !matchedGlobalIds.Contains(r.IfcGlobalId))
                    .Take(differenceCount)
                    .ToList();
            }

            HashSet<string> matchedUniqueIds = new HashSet<string>(
                ifcRows.Select(r => r.RevitUniqueId).Where(v => !string.IsNullOrWhiteSpace(v)),
                StringComparer.OrdinalIgnoreCase);

            if (useRevitUniqueIdOnly)
            {
                return revitRows
                    .Where(r => string.IsNullOrWhiteSpace(r.RevitUniqueId) || !matchedUniqueIds.Contains(r.RevitUniqueId))
                    .Take(differenceCount)
                    .ToList();
            }

            return revitRows.Take(differenceCount).ToList();
        }

        private static string JoinElementIds(IEnumerable<IfcReviewRow> rows)
        {
            return string.Join("; ", rows.Select(r => r.ElementId.ToString(CultureInfo.InvariantCulture)));
        }

        private static bool IfcRevitPropertiesMatch(IfcFileObjectRow row, string category, string family, string type)
        {
            bool typeMatches = TextEquals(row.RevitType, type) || IfcTextMatches(row, type);
            bool familyMatches = TextEquals(row.RevitFamily, family) || IfcTextMatches(row, family);
            bool categoryMatches = TextEquals(row.RevitCategory, category);

            if (typeMatches && (familyMatches || categoryMatches)) return true;
            if (familyMatches && categoryMatches) return true;
            return false;
        }

        private static bool IfcTextMatches(IfcFileObjectRow row, string expected)
        {
            expected = NormalizeText(expected);
            if (string.IsNullOrWhiteSpace(expected)) return false;

            string combined = NormalizeText(
                (row.Name ?? "") + " " +
                (row.ObjectType ?? "") + " " +
                (row.Description ?? "") + " " +
                (row.RevitFamily ?? "") + " " +
                (row.RevitType ?? ""));
            return combined.Contains(expected);
        }

        private static bool TextEquals(string left, string right)
        {
            string normalizedLeft = NormalizeText(left);
            string normalizedRight = NormalizeText(right);
            return normalizedLeft.Length > 0 && normalizedLeft == normalizedRight;
        }

        private static string NormalizeText(string value)
        {
            if (value == null) return "";
            return value.Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "")
                .Replace(":", "")
                .Replace("/", "")
                .Replace("\\", "")
                .Replace(".", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("[", "")
                .Replace("]", "");
        }

        private static string NormalizeIfcClass(string value)
        {
            value = value == null ? "" : value.Trim();
            if (value.Length == 0) return "UNKNOWN";
            if (value.StartsWith("Ifc", StringComparison.Ordinal))
                return value.ToUpperInvariant();
            if (value.StartsWith("IFC", StringComparison.Ordinal))
                return value.ToUpperInvariant();
            return ("Ifc" + value).ToUpperInvariant();
        }

        private static CategoryOption CreateCategoryOption(BuiltInCategory bic, string name)
        {
            return new CategoryOption { BuiltInCategoryId = (int)bic, Name = name, CheckedByDefault = true };
        }

        private static string GetIfcGlobalId(Document doc, ElementId elementId)
        {
            try
            {
                Guid exportId = ExportUtils.GetExportId(doc, elementId);
                return ToIfcGlobalId(exportId);
            }
            catch
            {
                return "";
            }
        }

        private static string ToIfcGlobalId(Guid guid)
        {
            string hex = guid.ToString("N");
            uint data1 = uint.Parse(hex.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint data2 = uint.Parse(hex.Substring(8, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint data3 = uint.Parse(hex.Substring(12, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint[] data4 = new uint[8];
            for (int i = 0; i < 8; i++)
                data4[i] = uint.Parse(hex.Substring(16 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return EncodeIfcBase64(data1 / 16777216, 2) +
                EncodeIfcBase64(data1 % 16777216, 4) +
                EncodeIfcBase64(data2 * 256 + data3 / 256, 4) +
                EncodeIfcBase64((data3 % 256) * 65536 + data4[0] * 256 + data4[1], 4) +
                EncodeIfcBase64(data4[2] * 65536 + data4[3] * 256 + data4[4], 4) +
                EncodeIfcBase64(data4[5] * 65536 + data4[6] * 256 + data4[7], 4);
        }

        private static string EncodeIfcBase64(uint value, int length)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            char[] result = new char[length];
            for (int i = length - 1; i >= 0; i--)
            {
                result[i] = chars[(int)(value % 64)];
                value /= 64;
            }
            return new string(result);
        }

        public int TryWriteRevitUniqueIdValues(Document doc, IEnumerable<int> categoryIds)
        {
            try
            {
                return WriteRevitUniqueIdValues(doc, categoryIds);
            }
            catch
            {
                return 0;
            }
        }

        public int WriteRevitUniqueIdValues(Document doc, IEnumerable<int> categoryIds)
        {
            var selected = new HashSet<int>(categoryIds);
            EnsureRevitUniqueIdParameter(doc, selected);

            int count = 0;
            using (Transaction tx = new Transaction(doc, "IFC Review Write RevitUniqueId"))
            {
                tx.Start();
                doc.Regenerate();
                foreach (FamilyInstance inst in new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance)))
                {
                    if (!IsSelectedCategory(inst.Category, selected)) continue;

                    Parameter param = inst.LookupParameter("RevitUniqueId");
                    if (param == null || param.IsReadOnly) continue;

                    param.Set(inst.UniqueId);
                    count++;
                }
                tx.Commit();
            }

            return count;
        }

        private static void EnsureRevitUniqueIdParameter(Document doc, HashSet<int> selected)
        {
            using (Transaction tx = new Transaction(doc, "IFC Review Bind RevitUniqueId"))
            {
                tx.Start();

                Definition definition = FindProjectParameterDefinition(doc, "RevitUniqueId");
                if (definition == null)
                    definition = CreateSharedTextDefinition(doc, "RevitUniqueId");

                CategorySet categorySet = doc.Application.Create.NewCategorySet();
                foreach (Category category in doc.Settings.Categories)
                {
                    if (category == null) continue;
                    if (!selected.Contains(category.Id.IntegerValue)) continue;
                    if (!category.AllowsBoundParameters) continue;
                    categorySet.Insert(category);
                }

                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categorySet);
                BindingMap map = doc.ParameterBindings;
                if (!map.Insert(definition, binding, BuiltInParameterGroup.PG_IDENTITY_DATA))
                    map.ReInsert(definition, binding, BuiltInParameterGroup.PG_IDENTITY_DATA);
                tx.Commit();
            }
        }

        private static Definition FindProjectParameterDefinition(Document doc, string name)
        {
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                if (definition != null && string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                    return definition;
            }
            return null;
        }

        private static Definition CreateSharedTextDefinition(Document doc, string name)
        {
            string previousSharedParameterFile = doc.Application.SharedParametersFilename;
            string tempPath = Path.Combine(Path.GetTempPath(), "IfcReviewAddin_shared_parameters.txt");
            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                File.WriteAllText(
                    tempPath,
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually.\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n",
                    System.Text.Encoding.UTF8);
            }

            try
            {
                doc.Application.SharedParametersFilename = tempPath;
                DefinitionFile file = doc.Application.OpenSharedParameterFile();
                if (file == null)
                    throw new InvalidOperationException("Could not open the temporary shared parameter file.");
                DefinitionGroup group = file.Groups.get_Item("IFC Review") ?? file.Groups.Create("IFC Review");
                Definition existing = group.Definitions.get_Item(name);
                if (existing != null)
                    return existing;

                var options = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text);
                return group.Definitions.Create(options);
            }
            finally
            {
                doc.Application.SharedParametersFilename = previousSharedParameterFile;
            }
        }

        private static IEnumerable<string> ReadIfcStatements(string ifcPath)
        {
            string statement = "";
            foreach (string rawLine in File.ReadLines(ifcPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                statement += line;
                if (line.EndsWith(";", StringComparison.Ordinal))
                {
                    yield return statement;
                    statement = "";
                }
            }
        }

        private static bool TryParseIfcEntity(string statement, out int stepId, out string ifcClass, out string argsText)
        {
            stepId = 0;
            ifcClass = "";
            argsText = "";

            if (!statement.StartsWith("#", StringComparison.Ordinal))
                return false;

            int equals = statement.IndexOf('=');
            int open = statement.IndexOf('(', equals + 1);
            int close = statement.LastIndexOf(')');
            if (equals < 2 || open < 0 || close <= open)
                return false;

            if (!int.TryParse(statement.Substring(1, equals - 1), out stepId))
                return false;

            ifcClass = statement.Substring(equals + 1, open - equals - 1).Trim().ToUpperInvariant();
            argsText = statement.Substring(open + 1, close - open - 1);
            return ifcClass.StartsWith("IFC", StringComparison.Ordinal);
        }

        private static bool LooksLikeExportedObject(string ifcClass, string globalId)
        {
            if (string.IsNullOrWhiteSpace(globalId)) return false;
            if (ifcClass.StartsWith("IFCREL", StringComparison.OrdinalIgnoreCase)) return false;
            if (ifcClass.StartsWith("IFCPROPERTY", StringComparison.OrdinalIgnoreCase)) return false;
            if (ifcClass.StartsWith("IFCELEMENTQUANTITY", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(ifcClass, "IFCPROJECT", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(ifcClass, "IFCSITE", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(ifcClass, "IFCBUILDING", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(ifcClass, "IFCBUILDINGSTOREY", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static List<string> SplitStepArguments(string argsText)
        {
            var args = new List<string>();
            int depth = 0;
            bool inString = false;
            int start = 0;

            for (int i = 0; i < argsText.Length; i++)
            {
                char c = argsText[i];
                if (c == '\'')
                {
                    if (inString && i + 1 < argsText.Length && argsText[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inString = !inString;
                    continue;
                }

                if (inString) continue;
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(argsText.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            args.Add(argsText.Substring(start).Trim());
            return args;
        }

        private static string ExtractStepString(string value)
        {
            value = value == null ? "" : value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return DecodeIfcString(value.Substring(1, value.Length - 2).Replace("''", "'"));
            return "";
        }

        private static string DecodeIfcString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var decoded = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                if (i + 4 <= value.Length &&
                    value[i] == '\\' &&
                    value[i + 1] == 'X' &&
                    value[i + 2] == '2' &&
                    value[i + 3] == '\\')
                {
                    int end = value.IndexOf("\\X0\\", i + 4, StringComparison.OrdinalIgnoreCase);
                    if (end > i)
                    {
                        string hex = value.Substring(i + 4, end - i - 4);
                        decoded.Append(DecodeUtf16BigEndianHex(hex));
                        i = end + 3;
                        continue;
                    }
                }

                if (i + 3 < value.Length && value[i] == '\\' && value[i + 1] == 'X' && value[i + 2] == '\\')
                {
                    string hex = value.Substring(i + 3, Math.Min(2, value.Length - i - 3));
                    int code;
                    if (hex.Length == 2 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                    {
                        decoded.Append((char)code);
                        i += 4;
                        continue;
                    }
                }

                decoded.Append(value[i]);
            }

            return decoded.ToString();
        }

        private static string DecodeUtf16BigEndianHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length % 4 != 0)
                return hex;

            var decoded = new StringBuilder();
            for (int i = 0; i + 3 < hex.Length; i += 4)
            {
                int code;
                if (!int.TryParse(hex.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                    return hex;
                decoded.Append((char)code);
            }
            return decoded.ToString();
        }

        private static string ExtractIfcSimpleValue(string value)
        {
            value = value == null ? "" : value.Trim();
            if (value == "$" || value == "*") return "";
            string directString = ExtractStepString(value);
            if (!string.IsNullOrWhiteSpace(directString))
                return directString;

            int open = value.IndexOf('(');
            int close = value.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                string inner = value.Substring(open + 1, close - open - 1).Trim();
                string typedString = ExtractStepString(inner);
                return !string.IsNullOrWhiteSpace(typedString) ? typedString : inner.Trim('.');
            }

            return value.Trim('.');
        }

        private static List<int> ExtractStepReferences(string value)
        {
            var ids = new List<int>();
            value = value == null ? "" : value.Trim();
            if (value.Length >= 2 && value[0] == '(' && value[value.Length - 1] == ')')
                value = value.Substring(1, value.Length - 2);

            foreach (string part in SplitStepArguments(value))
            {
                string text = part.Trim();
                if (!text.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int id;
                if (int.TryParse(text.Substring(1), out id))
                    ids.Add(id);
            }
            return ids;
        }

        private static string FindPredefinedType(List<string> args)
        {
            for (int i = args.Count - 1; i >= 0; i--)
            {
                string value = args[i].Trim();
                if (value.Length > 2 && value[0] == '.' && value[value.Length - 1] == '.')
                    return value.Trim('.');
            }
            return "";
        }

        private static bool IsSelectedCategory(Category category, HashSet<int> selected)
        {
            if (category == null) return false;
            return selected.Contains(category.Id.IntegerValue);
        }

        private static string GetParameterText(Element element, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter param = element.LookupParameter(name);
                if (param == null) continue;
                string value = param.AsValueString() ?? param.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static string GetIfcClassText(Element element)
        {
            return GetParameterText(
                element,
                "IfcExportAs",
                "IFCExportAs",
                "IFC Class",
                "IFCClass",
                "IfcClass",
                "Export to IFC As",
                "ExportToIFCAs");
        }

        private static Dictionary<string, string> GetParameterMap(Element element)
        {
            var values = new Dictionary<string, string>();
            foreach (Parameter param in element.Parameters)
            {
                try
                {
                    if (param.Definition == null) continue;
                    string name = param.Definition.Name;
                    if (values.ContainsKey(name)) continue;
                    values[name] = GetParameterValue(param);
                }
                catch { }
            }
            return values;
        }

        private static string GetParameterValue(Parameter param)
        {
            string value = param.AsValueString() ?? param.AsString();
            if (!string.IsNullOrEmpty(value)) return value;

            switch (param.StorageType)
            {
                case StorageType.Integer:
                    return param.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double:
                    return param.AsDouble().ToString(CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    return param.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static void WriteCsv(string path, IList<IfcReviewRow> rows)
        {
            string[] headers = new[]
            {
                "ElementId", "RevitUniqueId", "IfcGlobalId", "Category", "Family", "Type", "TypeId", "Level",
                "IFCClass", "IFCExportAs", "IFCExportType",
                "MissingIFCClass", "MissingIFCExportAs", "MissingIFCExportType", "Status"
            };

            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
                foreach (IfcReviewRow row in rows)
                {
                    var values = new object[]
                    {
                        row.ElementId,
                        row.RevitUniqueId,
                        row.IfcGlobalId,
                        row.Category,
                        row.Family,
                        row.Type,
                        row.TypeId,
                        row.Level,
                        row.IfcClass,
                        row.IfcExportAs,
                        row.IfcExportType,
                        row.MissingIfcClass,
                        row.MissingIfcExportAs,
                        row.MissingIfcExportType,
                        row.Status
                    };
                    writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
                }
            }
        }

        private static void WriteIfcFileObjectCsv(string path, IList<IfcFileObjectRow> rows)
        {
            string[] headers = new[]
            {
                "StepId", "IFCClass", "Name", "Description", "ObjectType", "PredefinedType",
                "RevitUniqueId", "RevitElementId", "RevitCategory", "RevitFamily", "RevitType", "Status"
            };

            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
                foreach (IfcFileObjectRow row in rows)
                {
                    var values = new object[]
                    {
                        row.StepId,
                        row.IfcClass,
                        row.Name,
                        row.Description,
                        row.ObjectType,
                        row.PredefinedType,
                        row.RevitUniqueId,
                        row.RevitElementId,
                        row.RevitCategory,
                        row.RevitFamily,
                        row.RevitType,
                        row.Status
                    };
                    writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
                }
            }
        }

        private static void WriteMissingCheckCsv(string path, IList<IfcMissingCheckRow> rows)
        {
            string[] headers = new[]
            {
                "Category", "Family", "Type", "ExpectedIFCClass", "RevitCount", "IFCClassCount", "SuspectedMissingCount", "Status"
            };

            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
                foreach (IfcMissingCheckRow row in rows)
                {
                    var values = new object[]
                    {
                        row.Category,
                        row.Family,
                        row.Type,
                        row.ExpectedIfcClass,
                        row.RevitCount,
                        row.IfcClassCount,
                        row.SuspectedMissingCount,
                        row.Status
                    };
                    writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
                }
            }
        }

        private static void WriteModelCompareCsv(string path, IList<IfcModelCompareRow> rows)
        {
            string[] headers = new[]
            {
                "Category", "Family", "Type", "ExpectedIFCClass", "RevitCount", "IFCMatchedCount", "Difference",
                "DifferenceElementIds", "RevitElementIds", "MatchMethod", "Status"
            };

            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
                foreach (IfcModelCompareRow row in rows)
                {
                    var values = new object[]
                    {
                        row.Category,
                        row.Family,
                        row.Type,
                        row.ExpectedIfcClass,
                        row.RevitCount,
                        row.IfcMatchedCount,
                        row.Difference,
                        row.DifferenceElementIds,
                        row.RevitElementIds,
                        row.MatchMethod,
                        row.Status
                    };
                    writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
                }
            }
        }

        private static void WriteJson(string path, object value)
        {
            string json = JsonConvert.SerializeObject(value, Formatting.Indented);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }

        private static string EscapeCsv(object value)
        {
            string text = value != null ? value.ToString() : "";
            if (text.Contains("\""))
                text = text.Replace("\"", "\"\"");
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\r") || text.Contains("\n"))
                text = "\"" + text + "\"";
            return text;
        }

        private static string GetModelBaseName(Document doc)
        {
            string value = !string.IsNullOrWhiteSpace(doc.PathName)
                ? Path.GetFileNameWithoutExtension(doc.PathName)
                : doc.Title;
            if (string.IsNullOrWhiteSpace(value))
                value = "revit_model";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        private static void EnsureFolder(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        private class IfcPropertyValue
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
