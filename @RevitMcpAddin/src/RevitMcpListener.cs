using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMcpAddin
{
    public class McpCommandHandler : IExternalEventHandler
    {
        private static readonly string CommDir = @"C:\revit_mcp";
        private static readonly string CommandFile = Path.Combine(CommDir, "command.json");
        private static readonly string ResultFile = Path.Combine(CommDir, "result.json");

        public string GetName()
        {
            return "RevitMcpCommandHandler";
        }

        public void Execute(UIApplication uiApp)
        {
            try
            {
                if (!File.Exists(CommandFile)) return;

                string raw = File.ReadAllText(CommandFile, System.Text.Encoding.UTF8);
                File.Delete(CommandFile);

                var cmd = JObject.Parse(raw);
                string tool = cmd["tool"] != null ? cmd["tool"].ToString() : "";
                var args = (cmd["args"] as JObject) ?? new JObject();

                var uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc != null ? uidoc.Document : null;
                if (doc == null)
                {
                    WriteResult(new { error = "No active Revit document." });
                    return;
                }

                object result;
                switch (tool)
                {
                    case "get_project_info":
                        result = GetProjectInfo(doc);
                        break;
                    case "get_all_categories":
                        result = GetAllCategories(doc);
                        break;
                    case "get_elements_by_category":
                        result = GetElementsByCategory(doc, args);
                        break;
                    case "get_element_by_id":
                        result = GetElementById(doc, args);
                        break;
                    case "set_parameter":
                        result = SetParameter(doc, args);
                        break;
                    case "create_walls_with_windows":
                        result = CreateWallsWithWindows(doc, args);
                        break;
                    case "review_electrical_families":
                        result = ReviewElectricalFamilies(doc, args);
                        break;
                    case "export_electrical_ifc":
                        result = ExportElectricalIfc(doc, args);
                        break;
                    default:
                        result = new { error = "Unknown command: " + tool };
                        break;
                }

                WriteResult(result);
            }
            catch (Exception ex)
            {
                WriteResult(new { error = ex.Message });
            }
        }

        private void WriteResult(object obj)
        {
            if (!Directory.Exists(CommDir))
                Directory.CreateDirectory(CommDir);

            string json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            File.WriteAllText(ResultFile, json, System.Text.Encoding.UTF8);
        }

        private object GetProjectInfo(Document doc)
        {
            var info = doc.ProjectInformation;
            return new
            {
                project_name = info.Name,
                project_number = info.Number,
                client_name = info.ClientName,
                address = info.Address,
                author = info.Author,
                building_name = info.BuildingName,
                organization_name = info.OrganizationName
            };
        }

        private object GetAllCategories(Document doc)
        {
            var seen = new HashSet<string>();
            var cats = new List<string>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (el.Category != null && !seen.Contains(el.Category.Name))
                {
                    seen.Add(el.Category.Name);
                    cats.Add(el.Category.Name);
                }
            }
            cats.Sort();
            return new { categories = cats };
        }

        private object GetElementsByCategory(Document doc, JObject args)
        {
            string categoryName = args["category"] != null ? args["category"].ToString() : "";
            int maxCount = args["max_count"] != null ? args["max_count"].ToObject<int>() : 20;

            var results = new List<object>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (el.Category == null) continue;
                if (el.Category.Name.IndexOf(categoryName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var parameters = new Dictionary<string, string>();
                foreach (Parameter p in el.Parameters)
                {
                    try
                    {
                        if (p.Definition == null) continue;
                        string val = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString();
                        parameters[p.Definition.Name] = val ?? "";
                    }
                    catch { }
                }

                results.Add(new { id = el.Id.IntegerValue, category = el.Category.Name, name = el.Name, parameters = parameters });
                if (results.Count >= maxCount) break;
            }

            return new { count = results.Count, elements = results };
        }

        private object GetElementById(Document doc, JObject args)
        {
            long elId = args["element_id"] != null ? args["element_id"].ToObject<long>() : 0;
            Element el = doc.GetElement(new ElementId(elId));
            if (el == null)
                return new { error = "Element not found: " + elId };

            var parameters = new Dictionary<string, string>();
            foreach (Parameter p in el.Parameters)
            {
                try
                {
                    if (p.Definition == null) continue;
                    string val = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString();
                    parameters[p.Definition.Name] = val ?? "";
                }
                catch { }
            }

            string catName = el.Category != null ? el.Category.Name : "Unknown";
            return new { id = el.Id.IntegerValue, category = catName, name = el.Name, parameters = parameters };
        }

        private object SetParameter(Document doc, JObject args)
        {
            long elId = args["element_id"] != null ? args["element_id"].ToObject<long>() : 0;
            string paramName = args["parameter_name"] != null ? args["parameter_name"].ToString() : "";
            string value = args["value"] != null ? args["value"].ToString() : "";

            Element el = doc.GetElement(new ElementId(elId));
            if (el == null)
                return new { success = false, error = "Element not found: " + elId };

            Parameter param = el.LookupParameter(paramName);
            if (param == null)
                return new { success = false, error = "Parameter not found: " + paramName };
            if (param.IsReadOnly)
                return new { success = false, error = "Parameter is read-only: " + paramName };

            using (Transaction tx = new Transaction(doc, "MCP SetParameter"))
            {
                tx.Start();
                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value);
                        break;
                    case StorageType.Double:
                        param.Set(double.Parse(value));
                        break;
                    case StorageType.Integer:
                        param.Set(int.Parse(value));
                        break;
                }
                tx.Commit();
            }

            return new { success = true, message = paramName + " set to " + value };
        }

        private object CreateWallsWithWindows(Document doc, JObject args)
        {
            int count = args["count"] != null ? args["count"].ToObject<int>() : 3;
            double heightMm = args["height_mm"] != null ? args["height_mm"].ToObject<double>() : 3000;
            double lengthMm = args["length_mm"] != null ? args["length_mm"].ToObject<double>() : 5000;
            double spacingMm = args["spacing_mm"] != null ? args["spacing_mm"].ToObject<double>() : 2000;

            double heightFt = heightMm / 304.8;
            double lengthFt = lengthMm / 304.8;
            double spacingFt = spacingMm / 304.8;

            var levelList = new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElements();
            if (levelList.Count == 0) return new { error = "No level exists in this project." };
            var level = (Level)levelList[0];

            var wallTypeList = new FilteredElementCollector(doc).OfClass(typeof(WallType)).ToElements();
            if (wallTypeList.Count == 0) return new { error = "No wall type exists in this project." };
            var wallType = (WallType)wallTypeList[0];

            var winList = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .ToElements();

            bool hasWindow = winList.Count > 0;
            FamilySymbol winSymbol = hasWindow ? (FamilySymbol)winList[0] : null;
            var created = new List<object>();

            using (Transaction tx = new Transaction(doc, "MCP CreateWallsWithWindows"))
            {
                tx.Start();

                if (hasWindow && !winSymbol.IsActive)
                    winSymbol.Activate();

                for (int i = 0; i < count; i++)
                {
                    double xStart = i * (lengthFt + spacingFt);
                    var line = Line.CreateBound(new XYZ(xStart, 0, 0), new XYZ(xStart + lengthFt, 0, 0));
                    Wall wall = Wall.Create(doc, line, wallType.Id, level.Id, heightFt, 0.0, false, false);

                    var info = new Dictionary<string, object>();
                    info["wall_id"] = wall.Id.IntegerValue;
                    info["wall_type"] = wallType.Name;

                    if (hasWindow)
                    {
                        var mid = new XYZ(xStart + lengthFt / 2.0, 0, 0);
                        var win = doc.Create.NewFamilyInstance(mid, winSymbol, wall, level, StructuralType.NonStructural);
                        info["window_id"] = win.Id.IntegerValue;
                        info["window_type"] = winSymbol.Family.Name;
                    }
                    else
                    {
                        info["window_note"] = "No window family is loaded in this project.";
                    }

                    created.Add(info);
                }

                tx.Commit();
            }

            return new
            {
                success = true,
                level = level.Name,
                wall_type = wallType.Name,
                height_mm = heightMm,
                count = count,
                walls = created
            };
        }

        private object ReviewElectricalFamilies(Document doc, JObject args)
        {
            string outputFolder = args["output_folder"] != null
                ? args["output_folder"].ToString()
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "revit_electrical_review");

            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var rows = BuildElectricalFamilyRows(doc, false);

            var summary = rows
                .GroupBy(r => new
                {
                    Category = r["Category"].ToString(),
                    Family = r["Family"].ToString(),
                    Type = r["Type"].ToString()
                })
                .Select(g => new
                {
                    category = g.Key.Category,
                    family = g.Key.Family,
                    type = g.Key.Type,
                    count = g.Count(),
                    missing_ifc_export_as = g.Count(r => (bool)r["MissingIFCExportAs"]),
                    missing_ifc_export_type = g.Count(r => (bool)r["MissingIFCExportType"])
                })
                .OrderBy(x => x.category)
                .ThenBy(x => x.family)
                .ThenBy(x => x.type)
                .ToList();

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csvPath = Path.Combine(outputFolder, "electrical_family_review_" + stamp + ".csv");
            WriteCsv(csvPath, rows);

            return new
            {
                success = true,
                output_csv = csvPath,
                instance_count = rows.Count,
                type_count = summary.Count,
                missing_ifc_export_as = rows.Count(r => (bool)r["MissingIFCExportAs"]),
                missing_ifc_export_type = rows.Count(r => (bool)r["MissingIFCExportType"]),
                summary = summary
            };
        }

        private object ExportElectricalIfc(Document doc, JObject args)
        {
            string outputFolder = args["output_folder"] != null
                ? args["output_folder"].ToString()
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "revit_electrical_ifc");
            string fileName = args["file_name"] != null
                ? args["file_name"].ToString()
                : SafeFileName(Path.GetFileNameWithoutExtension(doc.PathName));

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "electrical_families";
            if (!fileName.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
                fileName += ".ifc";
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string manifestCsvPath = Path.Combine(outputFolder, baseName + "_export_list.csv");
            string manifestJsonPath = Path.Combine(outputFolder, baseName + "_export_list.json");
            var exportRows = BuildElectricalFamilyRows(doc, true);
            WriteCsv(manifestCsvPath, exportRows);
            WriteJson(manifestJsonPath, new
            {
                source_model = doc.PathName,
                ifc_file = Path.Combine(outputFolder, fileName),
                exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                object_count = exportRows.Count,
                objects = exportRows
            });

            ElementId exportViewId;
            using (Transaction tx = new Transaction(doc, "MCP Create Electrical IFC View"))
            {
                tx.Start();
                ViewFamilyType viewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (viewType == null)
                    return new { success = false, error = "No 3D view family type exists in this project." };

                View3D view = View3D.CreateIsometric(doc, viewType.Id);
                view.Name = "MCP_Electrical_IFC_" + DateTime.Now.ToString("HHmmss");

                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    if (!cat.get_AllowsVisibilityControl(view)) continue;
                    try
                    {
                        view.SetCategoryHidden(cat.Id, !IsElectricalCategory(cat));
                    }
                    catch { }
                }

                exportViewId = view.Id;
                tx.Commit();
            }

            bool exported;
            string fullPath = Path.Combine(outputFolder, fileName);
            try
            {
                var options = new IFCExportOptions();
                options.FilterViewId = exportViewId;
                options.FileVersion = IFCVersion.IFC4;
                options.AddOption("ExportBaseQuantities", "true");
                options.AddOption("VisibleElementsOfCurrentView", "true");
                exported = doc.Export(outputFolder, fileName, options);
            }
            finally
            {
                using (Transaction tx = new Transaction(doc, "MCP Remove Electrical IFC View"))
                {
                    tx.Start();
                    doc.Delete(exportViewId);
                    tx.Commit();
                }
            }

            return new
            {
                success = exported,
                output_ifc = fullPath,
                output_list_csv = manifestCsvPath,
                output_list_json = manifestJsonPath,
                exported_object_count = exportRows.Count,
                note = "IFC was exported from a temporary 3D view with non-electrical categories hidden."
            };
        }

        private List<Dictionary<string, object>> BuildElectricalFamilyRows(Document doc, bool includeParameters)
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (FamilyInstance inst in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance)))
            {
                if (!IsElectricalCategory(inst.Category)) continue;

                FamilySymbol symbol = inst.Symbol;
                Family family = symbol != null ? symbol.Family : null;
                Level level = doc.GetElement(inst.LevelId) as Level;
                string ifcExportAs = GetParameterText(inst, "IfcExportAs", "IFCExportAs");
                string ifcExportType = GetParameterText(inst, "IfcExportType", "IFCExportType");
                if (string.IsNullOrWhiteSpace(ifcExportAs) && symbol != null)
                    ifcExportAs = GetParameterText(symbol, "IfcExportAs", "IFCExportAs");
                if (string.IsNullOrWhiteSpace(ifcExportType) && symbol != null)
                    ifcExportType = GetParameterText(symbol, "IfcExportType", "IFCExportType");

                var row = new Dictionary<string, object>
                {
                    { "ElementId", inst.Id.IntegerValue },
                    { "UniqueId", inst.UniqueId },
                    { "Category", inst.Category != null ? inst.Category.Name : "" },
                    { "Family", family != null ? family.Name : "" },
                    { "Type", symbol != null ? symbol.Name : "" },
                    { "TypeId", symbol != null ? symbol.Id.IntegerValue : -1 },
                    { "Level", level != null ? level.Name : "" },
                    { "IFCExportAs", ifcExportAs },
                    { "IFCExportType", ifcExportType },
                    { "MissingIFCExportAs", string.IsNullOrWhiteSpace(ifcExportAs) },
                    { "MissingIFCExportType", string.IsNullOrWhiteSpace(ifcExportType) }
                };

                if (includeParameters)
                {
                    row["InstanceParameters"] = GetParameterMap(inst);
                    row["TypeParameters"] = symbol != null
                        ? GetParameterMap(symbol)
                        : new Dictionary<string, string>();
                }

                rows.Add(row);
            }

            return rows;
        }

        private static bool IsElectricalCategory(Category category)
        {
            if (category == null) return false;
            BuiltInCategory bic;
            try
            {
                bic = (BuiltInCategory)category.Id.IntegerValue;
            }
            catch
            {
                return false;
            }

            switch (bic)
            {
                case BuiltInCategory.OST_ElectricalEquipment:
                case BuiltInCategory.OST_ElectricalFixtures:
                case BuiltInCategory.OST_LightingFixtures:
                case BuiltInCategory.OST_LightingDevices:
                case BuiltInCategory.OST_FireAlarmDevices:
                case BuiltInCategory.OST_CommunicationDevices:
                case BuiltInCategory.OST_DataDevices:
                case BuiltInCategory.OST_NurseCallDevices:
                case BuiltInCategory.OST_SecurityDevices:
                case BuiltInCategory.OST_TelephoneDevices:
                case BuiltInCategory.OST_CableTray:
                case BuiltInCategory.OST_CableTrayFitting:
                case BuiltInCategory.OST_Conduit:
                case BuiltInCategory.OST_ConduitFitting:
                    return true;
                default:
                    return false;
            }
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
                    values[name] = param.AsValueString() ?? param.AsString() ?? "";
                }
                catch { }
            }
            return values;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        private static void WriteCsv(string path, List<Dictionary<string, object>> rows)
        {
            string[] headers = new[]
            {
                "ElementId", "UniqueId", "Category", "Family", "Type", "TypeId", "Level",
                "IFCExportAs", "IFCExportType", "MissingIFCExportAs", "MissingIFCExportType"
            };

            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
                foreach (var row in rows)
                    writer.WriteLine(string.Join(",", headers.Select(h => EscapeCsv(row[h]))));
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
    }

    public static class RevitMcpListener
    {
        private static FileSystemWatcher _watcher;
        private static ExternalEvent _externalEvent;
        private static bool _started;

        public static void Initialize(UIControlledApplication app)
        {
            app.ControlledApplication.DocumentOpened += OnDocumentOpened;
        }

        public static void Shutdown()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            _externalEvent = null;
            _started = false;
        }

        private static void OnDocumentOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            if (_started) return;
            _started = true;

            _externalEvent = ExternalEvent.Create(new McpCommandHandler());

            string commDir = @"C:\revit_mcp";
            if (!Directory.Exists(commDir))
                Directory.CreateDirectory(commDir);

            _watcher = new FileSystemWatcher(commDir);
            _watcher.Filter = "command.json";
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            _watcher.Created += OnCommandFileCreated;
            _watcher.Changed += OnCommandFileCreated;
            _watcher.EnableRaisingEvents = true;
        }

        private static void OnCommandFileCreated(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(50);
            if (_externalEvent != null)
                _externalEvent.Raise();
        }
    }
}
