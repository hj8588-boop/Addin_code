using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace SharedParameterValuesExportAddin
{
    public static class ParameterExportService
    {
        public static ExportResult Export(Document document, ExportOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new InvalidOperationException("Output folder path is empty.");
            }

            string sheetName = string.IsNullOrWhiteSpace(options.SheetName) ? "SharedParameters" : options.SheetName.Trim();
            string filePath = ResolveOutputExcelPath(options.OutputPath, sheetName);
            IList<Element> elements = GetElements(document, options.Categories);
            IList<Element> filteredElements = elements
                .Where(element => ElementMatchesRequestedParameters(document, element, options.Parameters))
                .ToList();

            IList<ParameterSelection> parameters = options.Parameters == null || options.Parameters.Count == 0
                ? DiscoverSharedParameters(document, filteredElements)
                : options.Parameters;

            var rows = new List<IList<object>>();
            var headers = new List<object> { "ElementId", "UniqueId", "Category", "Family", "Type" };
            headers.AddRange(parameters.Select(parameter => (object)parameter.DisplayName));
            rows.Add(headers);

            foreach (Element element in filteredElements)
            {
                string familyName;
                string typeName;
                GetFamilyAndTypeNames(document, element, out familyName, out typeName);

                var row = new List<object>
                {
                    GetIntegerId(element.Id),
                    element.UniqueId,
                    element.Category == null ? string.Empty : element.Category.Name,
                    familyName,
                    typeName
                };

                foreach (ParameterSelection parameterSelection in parameters)
                {
                    Parameter parameter = GetSelectedParameter(document, element, parameterSelection);
                    row.Add(parameter == null || !parameter.IsShared ? string.Empty : GetParameterValue(parameter));
                }

                rows.Add(row);
            }

            XlsxWriter.Write(filePath, sheetName, rows, parameters);

            return new ExportResult
            {
                FilePath = filePath,
                SheetName = sheetName,
                CategoryCount = options.Categories.Count,
                ElementCount = filteredElements.Count,
                ParameterCount = parameters.Count,
                Parameters = parameters.Select(parameter => parameter.DisplayName).ToList()
            };
        }

        public static IList<ParameterSelection> DiscoverSharedParameters(Document document, IEnumerable<Category> categories)
        {
            return DiscoverSharedParameters(document, GetElements(document, categories));
        }

        public static IList<ParameterSelection> ParseParameterNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<ParameterSelection>();
            }

            var names = new List<ParameterSelection>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in value.Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                names.Add(new ParameterSelection(name, ParameterScope.Instance));
            }

            return names;
        }

        private static IList<Element> GetElements(Document document, IEnumerable<Category> categories)
        {
            var elements = new List<Element>();
            var seen = new HashSet<int>();

            foreach (Category category in categories)
            {
                if (category == null)
                {
                    continue;
                }

                try
                {
                    var collector = new FilteredElementCollector(document)
                        .OfCategoryId(category.Id)
                        .WhereElementIsNotElementType();

                    foreach (Element element in collector)
                    {
                        int id = GetIntegerId(element.Id);
                        if (seen.Add(id))
                        {
                            elements.Add(element);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return elements;
        }

        private static IList<ParameterSelection> DiscoverSharedParameters(Document document, IEnumerable<Element> elements)
        {
            var selections = new List<ParameterSelection>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ISet<string> projectParameterNames = GetProjectParameterNames(document);

            foreach (Element element in elements)
            {
                try
                {
                    AddSharedParameters(selections, seen, element, ParameterScope.Instance, projectParameterNames);

                    Element elementType = GetElementType(document, element);
                    AddSharedParameters(selections, seen, elementType, ParameterScope.Type, projectParameterNames);
                }
                catch
                {
                    continue;
                }
            }

            return selections
                .OrderBy(selection => selection.Name)
                .ThenBy(selection => selection.Scope.ToString())
                .ToList();
        }

        private static void AddSharedParameters(
            IList<ParameterSelection> selections,
            ISet<string> seen,
            Element element,
            ParameterScope scope,
            ISet<string> projectParameterNames)
        {
            if (element == null)
            {
                return;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                try
                {
                    if (parameter.IsShared && parameter.Definition != null && !string.IsNullOrWhiteSpace(parameter.Definition.Name))
                    {
                        string key = string.Format("{0}|{1}", scope, parameter.Definition.Name);
                        if (seen.Add(key))
                        {
                            ParameterOrigin origin = projectParameterNames.Contains(parameter.Definition.Name)
                                ? ParameterOrigin.Project
                                : ParameterOrigin.Family;

                            selections.Add(new ParameterSelection(parameter.Definition.Name, scope, origin));
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private static bool ElementMatchesRequestedParameters(Document document, Element element, IList<ParameterSelection> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            foreach (ParameterSelection selection in parameters)
            {
                Parameter parameter = GetSelectedParameter(document, element, selection);
                if (parameter != null && parameter.IsShared)
                {
                    return true;
                }
            }

            return false;
        }

        private static Parameter GetSelectedParameter(Document document, Element element, ParameterSelection selection)
        {
            if (selection == null || element == null)
            {
                return null;
            }

            Element sourceElement = selection.Scope == ParameterScope.Type
                ? GetElementType(document, element)
                : element;

            try
            {
                return sourceElement == null ? null : sourceElement.LookupParameter(selection.Name);
            }
            catch
            {
                return null;
            }
        }

        private static ISet<string> GetProjectParameterNames(Document document)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document == null || document.ParameterBindings == null)
            {
                return names;
            }

            DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Name))
                {
                    names.Add(definition.Name);
                }
            }

            return names;
        }

        private static Element GetElementType(Document document, Element element)
        {
            if (document == null || element == null)
            {
                return null;
            }

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

        private static void GetFamilyAndTypeNames(Document document, Element element, out string familyName, out string typeName)
        {
            familyName = string.Empty;
            typeName = string.Empty;

            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element elementType = document.GetElement(typeId);
                if (elementType != null)
                {
                    typeName = Normalize(elementType.Name);

                    Parameter symbolName = elementType.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM);
                    if (string.IsNullOrWhiteSpace(typeName) && symbolName != null)
                    {
                        typeName = Normalize(symbolName.AsString() ?? symbolName.AsValueString());
                    }

                    Parameter familyNameParameter = elementType.LookupParameter("Family Name");
                    if (familyNameParameter != null)
                    {
                        familyName = Normalize(familyNameParameter.AsString() ?? familyNameParameter.AsValueString());
                    }

                    ElementType typedElement = elementType as ElementType;
                    if (string.IsNullOrWhiteSpace(familyName) && typedElement != null)
                    {
                        familyName = Normalize(typedElement.FamilyName);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                Parameter typeParameter = element.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM);
                if (typeParameter != null)
                {
                    typeName = Normalize(typeParameter.AsValueString() ?? typeParameter.AsString());
                }
            }

            if (string.IsNullOrWhiteSpace(familyName))
            {
                Parameter familyParameter = element.LookupParameter("Family");
                if (familyParameter != null)
                {
                    familyName = Normalize(familyParameter.AsValueString() ?? familyParameter.AsString());
                }
            }
        }

        private static string GetParameterValue(Parameter parameter)
        {
            string displayValue = parameter.AsValueString();
            if (!string.IsNullOrEmpty(displayValue))
            {
                return displayValue;
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.Integer:
                    return parameter.AsInteger().ToString();
                case StorageType.Double:
                    return parameter.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    return GetIntegerId(parameter.AsElementId()).ToString();
                default:
                    return string.Empty;
            }
        }

        private static string ResolveOutputExcelPath(string pathValue, string sheetName)
        {
            string fullPath = Path.GetFullPath(pathValue.Trim());
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();

            if (Directory.Exists(fullPath))
            {
                return Path.Combine(fullPath, BuildTimestampedFileName(sheetName));
            }

            if (extension == ".xlsx" || extension == ".xlsm" || extension == ".xls")
            {
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return fullPath;
            }

            Directory.CreateDirectory(fullPath);
            return Path.Combine(fullPath, BuildTimestampedFileName(sheetName));
        }

        private static string BuildTimestampedFileName(string sheetName)
        {
            return string.Format(
                "SharedParameterValues_{0}_{1}.xlsx",
                GetSafeFileNamePart(sheetName),
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        private static string GetSafeFileNamePart(string value)
        {
            string text = Normalize(value);
            if (text.Length == 0)
            {
                return "SharedParameters";
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalidChar, '_');
            }

            return text.Trim(' ', '.') == string.Empty ? "SharedParameters" : text.Trim(' ', '.');
        }

        private static int GetIntegerId(ElementId id)
        {
#if REVIT2024_OR_LESS
#pragma warning disable 618
            return id.IntegerValue;
#pragma warning restore 618
#else
            return (int)id.Value;
#endif
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
