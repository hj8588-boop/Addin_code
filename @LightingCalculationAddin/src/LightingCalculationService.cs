using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Autodesk.Revit.DB;

namespace LightingCalculationAddin
{
    public static class LightingCalculationService
    {
        public const string RequiredLuxParam = "필수 요구조도";
        public const string UtilizationParam = "조명율";
        public const string MaintenanceParam = "보수율";
        public const string EffectiveHeightParam = "광원고";
        public const string CeilingReflectanceParam = "조도_천정반사율";
        public const string WallReflectanceParam = "조도_벽반사율";
        public const string FloorReflectanceParam = "조도_바닥반사율";
        public const string TargetFixtureTypeParam = "조도_적용전등타입";
        public const string RoomIndexParam = "조도_실지수";
        public const string RequiredCountParam = "필요등수";
        public const string ResultFluxParam = "fixtureFlux_lm";
        public const string ResultFixtureParam = "조도_적용기구";
        public const string InputFluxParam = "조도_광속_lm";
        private const double DefaultMaintenanceFactor = 1.0;

        private static readonly string[] FluxParamNames = new[]
        {
            InputFluxParam, ResultFluxParam, "광속", "광속(lm)", "Lamp Luminous Flux", "Initial Intensity"
        };

        public static IList<LightingSpaceRow> LoadRows(Document document, IList<LightingFixtureType> fixtureTypes)
        {
            var rows = new List<LightingSpaceRow>();
            foreach (Element space in CollectSpacesAndRooms(document))
            {
                if (ReadAreaM2(space) <= 0)
                {
                    continue;
                }

                var row = new LightingSpaceRow();
                row.SpaceId = space.Id;
                row.SpaceName = GetSpaceLabel(space);
                row.LevelName = GetLevelName(document, space);
                row.AreaM2 = ReadAreaM2(space);
                double length;
                double width;
                ReadPlanDimensionsM(space, out length, out width);
                row.LengthM = length;
                row.WidthM = width;
                row.RequiredLux = ReadDouble(space, RequiredLuxParam, 500);
                row.EffectiveHeightM = ReadDouble(space, EffectiveHeightParam, 2.4);
                row.CeilingReflectance = ReadDouble(space, CeilingReflectanceParam, 70);
                row.WallReflectance = ReadDouble(space, WallReflectanceParam, 50);
                row.FloorReflectance = ReadDouble(space, FloorReflectanceParam, 20);
                row.MaintenanceFactor = DefaultMaintenanceFactor;
                row.FixtureType = ReadString(space, TargetFixtureTypeParam, string.Empty);
                if (string.IsNullOrWhiteSpace(row.FixtureType))
                {
                    row.FixtureType = ReadString(space, ResultFixtureParam, string.Empty);
                }
                row.FixtureFluxLm = ReadDouble(space, InputFluxParam, 0);
                if (row.FixtureFluxLm <= 0)
                {
                    row.FixtureFluxLm = ReadDouble(space, ResultFluxParam, 0);
                }

                ApplyFixtureDefaults(row, fixtureTypes);
                Recalculate(row);
                rows.Add(row);
            }

            rows.Sort(delegate(LightingSpaceRow left, LightingSpaceRow right)
            {
                int level = string.Compare(left.LevelName, right.LevelName, StringComparison.CurrentCultureIgnoreCase);
                return level != 0 ? level : string.Compare(left.SpaceName, right.SpaceName, StringComparison.CurrentCultureIgnoreCase);
            });
            return rows;
        }

        private static IEnumerable<Element> CollectSpacesAndRooms(Document document)
        {
            var seen = new HashSet<int>();
            foreach (BuiltInCategory category in new[] { BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_Rooms })
            {
                foreach (Element element in new FilteredElementCollector(document)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (seen.Contains(element.Id.IntegerValue))
                    {
                        continue;
                    }

                    seen.Add(element.Id.IntegerValue);
                    yield return element;
                }
            }
        }

        public static IList<LightingFixtureType> LoadFixtureTypes(Document document)
        {
            var items = new List<LightingFixtureType>();
            var seen = new HashSet<int>();

            foreach (Element instance in new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType())
            {
                Element type = document.GetElement(instance.GetTypeId());
                if (type == null || seen.Contains(type.Id.IntegerValue))
                {
                    continue;
                }

                string familyName = GetFamilyName(type, instance);
                string typeName = GetTypeName(type, instance);
                double flux;
                string fluxParamName;
                ReadFixtureFlux(type, instance, out flux, out fluxParamName);

                var item = new LightingFixtureType();
                item.TypeId = type.Id;
                item.FamilyName = familyName;
                item.TypeName = typeName;
                item.Label = familyName + " : " + typeName;
                item.FluxLm = flux;
                item.FluxParameterName = fluxParamName;
                items.Add(item);
                seen.Add(type.Id.IntegerValue);
            }

            items.Sort(delegate(LightingFixtureType left, LightingFixtureType right)
            {
                return string.Compare(left.Label, right.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return items;
        }

        public static void ApplyFixtureDefaults(LightingSpaceRow row, IList<LightingFixtureType> fixtureTypes)
        {
            LightingFixtureType fixture = FindFixture(fixtureTypes, row.FixtureType);
            if (fixture != null)
            {
                row.FixtureType = fixture.Label;
                if (row.FixtureFluxLm <= 0 && fixture.FluxLm > 0)
                {
                    row.FixtureFluxLm = fixture.FluxLm;
                }
            }
        }

        public static LightingFixtureType FindFixture(IList<LightingFixtureType> fixtures, string label)
        {
            string key = Normalize(label);
            foreach (LightingFixtureType fixture in fixtures)
            {
                if (Normalize(fixture.Label) == key || Normalize(fixture.TypeName) == key || Normalize(fixture.FamilyName) == key)
                {
                    return fixture;
                }
            }

            return null;
        }

        public static void Recalculate(LightingSpaceRow row)
        {
            row.RoomIndex = CalculateRoomIndex(row.AreaM2, row.LengthM, row.WidthM, row.EffectiveHeightM);
            double autoUtilization = GetUtilizationFromRoomIndex(row.RoomIndex);
            row.UtilizationFactor = autoUtilization > 0 ? autoUtilization : row.UtilizationFactor;

            var missing = new List<string>();
            if (row.AreaM2 <= 0) missing.Add("면적");
            if (row.RequiredLux <= 0) missing.Add(RequiredLuxParam);
            if (row.EffectiveHeightM <= 0) missing.Add(EffectiveHeightParam);
            if (row.UtilizationFactor <= 0) missing.Add(UtilizationParam);
            if (row.MaintenanceFactor <= 0) missing.Add(MaintenanceParam);
            if (string.IsNullOrWhiteSpace(row.FixtureType)) missing.Add(TargetFixtureTypeParam);
            if (row.FixtureFluxLm <= 0) missing.Add(InputFluxParam + "/" + ResultFluxParam);

            if (missing.Count > 0)
            {
                row.RawRequiredCount = 0;
                row.RequiredCount = 0;
                row.CalculatedIlluminance = 0;
                row.Status = "확인필요";
                row.Message = "누락: " + string.Join(", ", missing.ToArray());
                return;
            }

            row.RawRequiredCount = (row.RequiredLux * row.AreaM2) / (row.FixtureFluxLm * row.UtilizationFactor * row.MaintenanceFactor);
            row.RequiredCount = (int)Math.Ceiling(row.RawRequiredCount);
            row.CalculatedIlluminance = (row.FixtureFluxLm * row.UtilizationFactor * row.MaintenanceFactor * row.RequiredCount) / row.AreaM2;
            row.Status = "OK";
            row.Message = string.Empty;
        }

        public static int WriteFluxToTypeInstances(Document document, ElementId typeId, double fluxLm)
        {
            int count = 0;
            using (var transaction = new Transaction(document, "조명 광속 직접 입력"))
            {
                transaction.Start();
                Element type = document.GetElement(typeId);
                if (type != null && TrySetParameter(type, ResultFluxParam, fluxLm))
                {
                    count++;
                }

                foreach (Element instance in new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType())
                {
                    if (instance.GetTypeId().IntegerValue != typeId.IntegerValue)
                    {
                        continue;
                    }

                    if (TrySetParameter(instance, ResultFluxParam, fluxLm))
                    {
                        count++;
                    }
                }
                transaction.Commit();
            }
            return count;
        }

        public static int SyncFixtureFluxToProject(Document document)
        {
            var typeFluxMap = new Dictionary<int, double>();

            foreach (Element instance in new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType())
            {
                int typeKey = instance.GetTypeId().IntegerValue;
                if (typeFluxMap.ContainsKey(typeKey))
                {
                    continue;
                }

                Element type = document.GetElement(instance.GetTypeId());
                double flux;
                string unused;
                ReadFixtureFlux(type, instance, out flux, out unused);
                if (flux > 0)
                {
                    typeFluxMap[typeKey] = flux;
                }
            }

            if (typeFluxMap.Count == 0)
            {
                return 0;
            }

            int count = 0;
            using (var transaction = new Transaction(document, "조명 광속 동기화"))
            {
                transaction.Start();

                foreach (KeyValuePair<int, double> kv in typeFluxMap)
                {
                    Element type = document.GetElement(new ElementId(kv.Key));
                    if (type == null)
                    {
                        continue;
                    }

                    if (TrySetParameter(type, ResultFluxParam, kv.Value))
                    {
                        count++;
                    }
                }

                foreach (Element instance in new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType())
                {
                    int typeKey = instance.GetTypeId().IntegerValue;
                    double flux;
                    if (!typeFluxMap.TryGetValue(typeKey, out flux))
                    {
                        continue;
                    }

                    if (TrySetParameter(instance, ResultFluxParam, flux))
                    {
                        count++;
                    }
                }

                transaction.Commit();
            }

            return count;
        }

        public static void SaveRows(Document document, IEnumerable<LightingSpaceRow> rows)
        {
            using (var transaction = new Transaction(document, "조도 계산서 저장"))
            {
                transaction.Start();
                foreach (LightingSpaceRow row in rows)
                {
                    Element space = document.GetElement(row.SpaceId);
                    if (space == null)
                    {
                        continue;
                    }

                    SetParameter(space, RequiredLuxParam, row.RequiredLux);
                    SetParameter(space, EffectiveHeightParam, row.EffectiveHeightM);
                    SetParameter(space, CeilingReflectanceParam, row.CeilingReflectance);
                    SetParameter(space, WallReflectanceParam, row.WallReflectance);
                    SetParameter(space, FloorReflectanceParam, row.FloorReflectance);
                    SetParameter(space, MaintenanceParam, row.MaintenanceFactor);
                    SetParameter(space, UtilizationParam, row.UtilizationFactor);
                    SetParameter(space, TargetFixtureTypeParam, row.FixtureType);
                    SetParameter(space, InputFluxParam, row.FixtureFluxLm);
                    SetParameter(space, ResultFluxParam, row.FixtureFluxLm);
                    SetParameter(space, RoomIndexParam, row.RoomIndex);
                    SetParameter(space, RequiredCountParam, row.RequiredCount);
                    SetParameter(space, ResultFixtureParam, row.FixtureType);
                }
                transaction.Commit();
            }
        }

        private static double CalculateRoomIndex(double areaM2, double lengthM, double widthM, double effectiveHeightM)
        {
            if (areaM2 <= 0 || lengthM <= 0 || widthM <= 0 || effectiveHeightM <= 0)
            {
                return 0;
            }

            double denominator = effectiveHeightM * (lengthM + widthM);
            return denominator <= 0 ? 0 : areaM2 / denominator;
        }

        private static double GetUtilizationFromRoomIndex(double roomIndex)
        {
            if (roomIndex <= 0) return 0;
            if (roomIndex <= 0.75) return 0.60;
            if (roomIndex <= 1.00) return 0.68;
            if (roomIndex <= 1.25) return 0.76;
            if (roomIndex <= 1.50) return 0.81;
            if (roomIndex <= 2.00) return 0.88;
            if (roomIndex <= 2.50) return 0.93;
            if (roomIndex <= 3.00) return 0.96;
            return 1.01;
        }

        private static string GetSpaceLabel(Element space)
        {
            string number = ReadString(space, "Number", string.Empty);
            string name = GetSpaceName(space);
            if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(name))
            {
                return number + " - " + name;
            }

            return string.IsNullOrWhiteSpace(name) ? "ElementId " + space.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) : name;
        }

        private static string GetSpaceName(Element space)
        {
            string value = ReadString(space, "Name", string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            try
            {
                return space.Name;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetLevelName(Document document, Element space)
        {
            try
            {
                PropertyInfo property = space.GetType().GetProperty("LevelId");
                ElementId levelId = property == null ? ElementId.InvalidElementId : property.GetValue(space, null) as ElementId;
                Element level = document.GetElement(levelId);
                return level == null ? string.Empty : level.Name;
            }
            catch
            {
                return ReadString(space, "Level", string.Empty);
            }
        }

        private static double ReadAreaM2(Element space)
        {
            try
            {
                PropertyInfo property = space.GetType().GetProperty("Area");
                if (property == null)
                {
                    return 0;
                }

                double internalArea = Convert.ToDouble(property.GetValue(space, null), CultureInfo.InvariantCulture);
                return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
            }
            catch
            {
                return 0;
            }
        }

        private static void ReadPlanDimensionsM(Element space, out double lengthM, out double widthM)
        {
            lengthM = 0;
            widthM = 0;
            BoundingBoxXYZ box = space.get_BoundingBox(null);
            if (box == null)
            {
                return;
            }

            double dx = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.X - box.Min.X), UnitTypeId.Meters);
            double dy = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Y - box.Min.Y), UnitTypeId.Meters);
            lengthM = Math.Max(dx, dy);
            widthM = Math.Min(dx, dy);
        }

        private static string GetFamilyName(Element type, Element instance)
        {
            try
            {
                ElementType elementType = type as ElementType;
                if (elementType != null)
                {
                    return elementType.FamilyName;
                }
            }
            catch
            {
            }

            return instance == null ? "UnknownFamily" : instance.Name;
        }

        private static string GetTypeName(Element type, Element instance)
        {
            Parameter parameter = type == null ? null : type.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM);
            string value = parameter == null ? null : parameter.AsString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return type == null ? (instance == null ? "UnknownType" : instance.Name) : type.Name;
        }

        private static void ReadFixtureFlux(Element type, Element instance, out double flux, out string parameterName)
        {
            flux = 0;
            parameterName = string.Empty;
            foreach (Element target in new[] { type, instance })
            {
                if (target == null)
                {
                    continue;
                }

                foreach (string name in FluxParamNames)
                {
                    double value = ReadFirstPositiveDouble(target, name);
                    if (value > 0)
                    {
                        flux = value;
                        parameterName = name;
                        return;
                    }
                }
            }
        }

        private static double ReadFirstPositiveDouble(Element element, string parameterName)
        {
            if (element == null)
            {
                return 0;
            }

            try
            {
                // 1차: GetParameters (공유 파라미터, 빌트인 파라미터)
                IList<Parameter> byName = element.GetParameters(parameterName);
                foreach (Parameter parameter in byName)
                {
                    double value = ExtractPositiveDouble(parameter);
                    if (value > 0)
                    {
                        return value;
                    }
                }

                // 2차: element.Parameters 전체 순회 (비공유 패밀리 파라미터 포함)
                foreach (Parameter parameter in element.Parameters)
                {
                    if (parameter.Definition == null)
                    {
                        continue;
                    }

                    if (!string.Equals(parameter.Definition.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double value = ExtractPositiveDouble(parameter);
                    if (value > 0)
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static double ExtractPositiveDouble(Parameter parameter)
        {
            try
            {
                double valueFromDisplay = ParseFirstNumber(parameter.AsValueString());
                if (valueFromDisplay > 0)
                {
                    return valueFromDisplay;
                }

                if (parameter.StorageType == StorageType.Double)
                {
                    return parameter.AsDouble();
                }

                if (parameter.StorageType == StorageType.Integer)
                {
                    return parameter.AsInteger();
                }

                if (parameter.StorageType == StorageType.String)
                {
                    double parsed = ParseFirstNumber(parameter.AsString());
                    if (parsed > 0)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static double ParseFirstNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string normalized = text.Trim();
            double parsed;
            if (double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
            {
                return parsed;
            }

            if (double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            var buffer = new List<char>();
            foreach (char c in normalized)
            {
                if (char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+')
                {
                    buffer.Add(c);
                }
                else if (buffer.Count > 0)
                {
                    break;
                }
            }

            if (buffer.Count == 0)
            {
                return 0;
            }

            string numericText = new string(buffer.ToArray()).Replace(",", string.Empty);
            return double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static double ReadDouble(Element element, string parameterName, double defaultValue)
        {
            Parameter parameter = element == null ? null : element.LookupParameter(parameterName);
            if (parameter == null)
            {
                return defaultValue;
            }

            try
            {
                if (parameter.StorageType == StorageType.Double)
                {
                    return parameter.AsDouble();
                }

                if (parameter.StorageType == StorageType.Integer)
                {
                    return parameter.AsInteger();
                }

                if (parameter.StorageType == StorageType.String)
                {
                    double value;
                    return double.TryParse(parameter.AsString(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) ? value : defaultValue;
                }
            }
            catch
            {
            }

            return defaultValue;
        }

        private static string ReadString(Element element, string parameterName, string defaultValue)
        {
            Parameter parameter = element == null ? null : element.LookupParameter(parameterName);
            if (parameter == null)
            {
                return defaultValue;
            }

            try
            {
                if (parameter.StorageType == StorageType.String)
                {
                    return parameter.AsString() ?? defaultValue;
                }

                return parameter.AsValueString() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static void SetParameter(Element element, string parameterName, object value)
        {
            TrySetParameter(element, parameterName, value);
        }

        private static bool TrySetParameter(Element element, string parameterName, object value)
        {
            Parameter parameter = FindWritableParameter(element, parameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                return false;
            }

            try
            {
                if (parameter.StorageType == StorageType.Double)
                {
                    parameter.Set(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                }
                else if (parameter.StorageType == StorageType.Integer)
                {
                    parameter.Set(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                }
                else if (parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value == null ? string.Empty : value.ToString());
                }

                return true;
            }
            catch
            {
            }

            return false;
        }

        private static Parameter FindWritableParameter(Element element, string parameterName)
        {
            if (element == null)
            {
                return null;
            }

            IList<Parameter> byName = element.GetParameters(parameterName);
            foreach (Parameter parameter in byName)
            {
                if (parameter != null && !parameter.IsReadOnly)
                {
                    return parameter;
                }
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter == null || parameter.Definition == null || parameter.IsReadOnly)
                {
                    continue;
                }

                if (string.Equals(parameter.Definition.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
