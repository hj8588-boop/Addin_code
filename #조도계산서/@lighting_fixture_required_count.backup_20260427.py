import csv
import math
import os
from datetime import datetime

import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    BuiltInCategory,
    BuiltInParameter,
    ElementId,
    FamilyInstance,
    StorageType,
    UnitUtils,
)
from Autodesk.Revit.DB.Architecture import Room

try:
    from Autodesk.Revit.DB.Mechanical import Space
except Exception:
    Space = None

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager


doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
uidoc = uiapp.ActiveUIDocument if uiapp else None


RUN_GRAPH = bool(IN[0]) if len(IN) > 0 else False
EXPORT_PATH_INPUT = IN[1] if len(IN) > 1 else None

ROOM_REQUIRED_LUX_PARAM = "필수 요구조도"
ROOM_UTILIZATION_PARAM = "조명율"
ROOM_MAINTENANCE_PARAM = "보수율"
ROOM_REFLECTANCE_PARAM = "조도_반사율"
ROOM_RESULT_COUNT_PARAM = "필요등수"
ROOM_RESULT_FLUX_PARAM = "fixtureFlux_lm"
ROOM_RESULT_FIXTURE_PARAM = "조도_적용기구"

FIXTURE_FLUX_PARAM = "fixtureFlux_lm"

COMMON_FLUX_PARAM_NAMES = [
    FIXTURE_FLUX_PARAM,
    "광속",
    "광속(lm)",
    "Lamp Luminous Flux",
    "Initial Intensity",
]

SCRIPT_PATH = globals().get("__file__", r"C:\Users\user\Desktop\codex\#조도계산서\@lighting_fixture_required_count.py")
SCRIPT_DIR = os.path.dirname(SCRIPT_PATH)
PRIMARY_EXPORT_DIR = os.path.join(SCRIPT_DIR, "exports")

ROOM_INDEX_PARAM = "조도_실지수"
ROOM_EFFECTIVE_HEIGHT_PARAM = "광원고"

CSV_FIELD_MAPPINGS = [
    ("roomId", "roomId_\uacf5\uac04ID"),
    ("room", "room_\uc2e4\uba85"),
    ("roomNameParamValue", "roomNameParamValue_room name"),
    ("levelName", "levelName_\ub808\ubca8"),
    ("status", "status_\uc0c1\ud0dc"),
    ("message", "message_\uba54\uc2dc\uc9c0"),
    ("area_m2", "area_m2_\uba74\uc801"),
    ("length_m", "length_m_\uac00\ub85c"),
    ("width_m", "width_m_\uc138\ub85c"),
    ("effectiveHeight_m", "effectiveHeight_m_\uad11\uc6d0\uace0"),
    ("roomIndex", "roomIndex_\uc2e4\uc9c0\uc218"),
    ("requiredLux", "requiredLux_\uc694\uad6c\uc870\ub3c4"),
    ("utilizationFactor", "utilizationFactor_\uc870\uba85\uc728"),
    ("maintenanceFactor", "maintenanceFactor_\ubcf4\uc218\uc728"),
    ("reflectance", "reflectance_\ubc18\uc0ac\uc728"),
    ("wallReflectance", "wallReflectance_\ubcbd\ubc18\uc0ac\uc728"),
    ("ceilingReflectance", "ceilingReflectance_\ucc9c\uc7a5\ubc18\uc0ac\uc728"),
    ("floorReflectance", "floorReflectance_\ubc14\ub2e5\ubc18\uc0ac\uc728"),
    ("fixture", "fixture_\uae30\uad6c"),
    ("fixtureFlux_lm", "fixtureFlux_lm_\uad11\uc18d"),
    ("rawRequiredCount", "rawRequiredCount_\uacc4\uc0b0\ub4f1\uc218"),
    ("requiredCount", "requiredCount_\ud544\uc694\ub4f1\uc218"),
    ("calculatedIlluminance", "calculatedIlluminance_\uacc4\uc0b0\uc870\ub3c4"),
    ("requiredLuxParam", "requiredLuxParam_\uc694\uad6c\uc870\ub3c4\ud30c\ub77c\ubbf8\ud130"),
    ("utilizationParam", "utilizationParam_\uc870\uba85\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("maintenanceParam", "maintenanceParam_\ubcf4\uc218\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("reflectanceParam", "reflectanceParam_\ubc18\uc0ac\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("wallReflectanceParam", "wallReflectanceParam_\ubcbd\ubc18\uc0ac\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("ceilingReflectanceParam", "ceilingReflectanceParam_\ucc9c\uc7a5\ubc18\uc0ac\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("floorReflectanceParam", "floorReflectanceParam_\ubc14\ub2e5\ubc18\uc0ac\uc728\ud30c\ub77c\ubbf8\ud130"),
    ("fixtureFluxParam", "fixtureFluxParam_\uad11\uc18d\ud30c\ub77c\ubbf8\ud130"),
]

CSV_GROUP_LABELS = {
    "area_m2": "\uc2e4\uc758 \uc870\uac74",
    "length_m": "\uc2e4\uc758 \uc870\uac74",
    "width_m": "\uc2e4\uc758 \uc870\uac74",
    "effectiveHeight_m": "\uc2e4\uc758 \uc870\uac74",
    "fixture": "\ub4f1\uae30\uad6c \uc0ac\uc591",
    "fixtureFlux_lm": "\ub4f1\uae30\uad6c \uc0ac\uc591",
    "wallReflectance": "\ubc18\uc0ac\uc728",
    "ceilingReflectance": "\ubc18\uc0ac\uc728",
    "floorReflectance": "\ubc18\uc0ac\uc728",
}

COMMON_REFLECTANCE_PARAM_NAMES = [
    ROOM_REFLECTANCE_PARAM,
    "\ubc18\uc0ac\uc728",
    "Reflectance",
]

WALL_REFLECTANCE_PARAM_NAMES = [
    "\ubcbd\ubc18\uc0ac\uc728",
    "\ubcbd \ubc18\uc0ac\uc728",
    "Wall Reflectance",
]

CEILING_REFLECTANCE_PARAM_NAMES = [
    "\ucc9c\uc7a5\ubc18\uc0ac\uc728",
    "\ucc9c\uc815\ubc18\uc0ac\uc728",
    "\ucc9c\uc7a5 \ubc18\uc0ac\uc728",
    "Ceiling Reflectance",
]

FLOOR_REFLECTANCE_PARAM_NAMES = [
    "\ubc14\ub2e5\ubc18\uc0ac\uc728",
    "\ubc14\ub2e5 \ubc18\uc0ac\uc728",
    "Floor Reflectance",
]


def normalize_export_input_path(path_text):
    path_text = safe_string(path_text).strip()
    if not path_text:
        return None

    path_text = path_text.strip('"').strip("'")

    if path_text.lower().endswith(".csv"):
        return os.path.dirname(path_text), path_text

    return path_text, None


def get_export_directories():
    directories = []

    input_directory, input_file_path = normalize_export_input_path(EXPORT_PATH_INPUT)
    if input_directory:
        directories.append(input_directory)

    directories.append(PRIMARY_EXPORT_DIR)

    user_profile = os.environ.get("USERPROFILE")
    if user_profile:
        directories.append(os.path.join(user_profile, "Documents", "조도계산서_exports"))

    temp_dir = os.environ.get("TEMP")
    if temp_dir:
        directories.append(os.path.join(temp_dir, "조도계산서_exports"))

    unique_directories = []
    for directory in directories:
        if directory and directory not in unique_directories:
            unique_directories.append(directory)

    return unique_directories


def to_square_meters(area_internal):
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertFromInternalUnits(area_internal, UnitTypeId.SquareMeters)
    except Exception:
        return float(area_internal) * 0.09290304


def to_meters(length_internal):
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertFromInternalUnits(length_internal, UnitTypeId.Meters)
    except Exception:
        return float(length_internal) * 0.3048


def get_selected_elements():
    if uidoc is None:
        return []
    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def is_room_or_space(element):
    if element is None:
        return False
    if isinstance(element, Room):
        return True
    if Space is not None and isinstance(element, Space):
        return True
    try:
        category_id = element.Category.Id.IntegerValue
    except Exception:
        return False
    return category_id in [
        int(BuiltInCategory.OST_Rooms),
        int(BuiltInCategory.OST_MEPSpaces),
    ]


def is_lighting_fixture(element):
    if not isinstance(element, FamilyInstance):
        return False
    try:
        return element.Category.Id.IntegerValue == int(BuiltInCategory.OST_LightingFixtures)
    except Exception:
        return False


def get_parameter(element, name):
    if element is None or not name:
        return None
    try:
        parameter = element.LookupParameter(name)
        if parameter is not None:
            return parameter
    except Exception:
        pass
    return None


def get_parameter_value(parameter):
    if parameter is None:
        return None
    try:
        if parameter.StorageType == StorageType.Double:
            return parameter.AsDouble()
        if parameter.StorageType == StorageType.Integer:
            return parameter.AsInteger()
        if parameter.StorageType == StorageType.String:
            return parameter.AsString()
        if parameter.StorageType == StorageType.ElementId:
            value = parameter.AsElementId()
            return value.IntegerValue if isinstance(value, ElementId) else None
    except Exception:
        return None
    return None


def as_float(value):
    try:
        return float(value)
    except Exception:
        return None


def read_numeric_parameter(element, param_names):
    if isinstance(param_names, str):
        param_names = [param_names]

    for param_name in param_names:
        parameter = get_parameter(element, param_name)
        value = as_float(get_parameter_value(parameter))
        if value is not None:
            return value, param_name
    return None, None


def read_string_parameter(element, param_names):
    if isinstance(param_names, str):
        param_names = [param_names]

    for param_name in param_names:
        parameter = get_parameter(element, param_name)
        value = get_parameter_value(parameter)
        text = safe_string(value).strip()
        if text:
            return text, param_name
    return None, None


def get_fixture_type(element):
    try:
        return doc.GetElement(element.GetTypeId())
    except Exception:
        return None


def get_fixture_label(fixture_instance):
    fixture_type = get_fixture_type(fixture_instance)
    try:
        family_name = fixture_type.FamilyName
    except Exception:
        try:
            family_name = fixture_instance.Symbol.Family.Name
        except Exception:
            family_name = "UnknownFamily"

    try:
        type_name = fixture_type.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM).AsString()
    except Exception:
        try:
            type_name = fixture_instance.Name
        except Exception:
            type_name = "UnknownType"

    return "{0} : {1}".format(family_name, type_name)


def read_fixture_flux(fixture_instance):
    fixture_type = get_fixture_type(fixture_instance)
    search_targets = [fixture_type, fixture_instance]

    for search_target in search_targets:
        if search_target is None:
            continue
        for param_name in COMMON_FLUX_PARAM_NAMES:
            value, found_name = read_numeric_parameter(search_target, [param_name])
            if value is not None:
                return value, found_name

    return None, None


def read_room_area(room_or_space):
    try:
        return to_square_meters(room_or_space.Area)
    except Exception:
        pass

    area_param, _ = read_numeric_parameter(room_or_space, ["Area", "면적"])
    if area_param is not None:
        return area_param
    return None


def read_room_plan_dimensions(room_or_space):
    try:
        bounding_box = room_or_space.get_BoundingBox(None)
    except Exception:
        bounding_box = None

    if bounding_box is None:
        return None, None

    try:
        delta_x = abs(bounding_box.Max.X - bounding_box.Min.X)
        delta_y = abs(bounding_box.Max.Y - bounding_box.Min.Y)
        dimensions = sorted([to_meters(delta_x), to_meters(delta_y)], reverse=True)
        return dimensions[0], dimensions[1]
    except Exception:
        return None, None


def read_reflectance_parameter(room_or_space):
    return read_numeric_parameter(room_or_space, COMMON_REFLECTANCE_PARAM_NAMES)


def read_surface_reflectance_parameters(room_or_space):
    wall_reflectance, wall_param = read_numeric_parameter(room_or_space, WALL_REFLECTANCE_PARAM_NAMES)
    ceiling_reflectance, ceiling_param = read_numeric_parameter(room_or_space, CEILING_REFLECTANCE_PARAM_NAMES)
    floor_reflectance, floor_param = read_numeric_parameter(room_or_space, FLOOR_REFLECTANCE_PARAM_NAMES)

    return {
        "wallReflectance": wall_reflectance,
        "wallReflectanceParam": wall_param,
        "ceilingReflectance": ceiling_reflectance,
        "ceilingReflectanceParam": ceiling_param,
        "floorReflectance": floor_reflectance,
        "floorReflectanceParam": floor_param,
    }


def calculate_room_index(area_m2, length_m, width_m, effective_height_m):
    if (
        area_m2 is None
        or area_m2 <= 0
        or length_m is None
        or width_m is None
        or effective_height_m is None
        or length_m <= 0
        or width_m <= 0
        or effective_height_m <= 0
    ):
        return None

    denominator = effective_height_m * (length_m + width_m)
    if denominator <= 0:
        return None

    return area_m2 / denominator


def get_utilization_from_room_index(room_index):
    if room_index is None:
        return None
    if room_index <= 0.75:
        return 0.60
    if room_index <= 1:
        return 0.68
    if room_index <= 1.25:
        return 0.76
    if room_index <= 1.5:
        return 0.81
    if room_index <= 2:
        return 0.88
    if room_index <= 2.5:
        return 0.93
    if room_index <= 3:
        return 0.96
    return 1.01


def safe_string(value):
    if value is None:
        return ""
    return str(value)


def set_parameter_value(parameter, value):
    if parameter is None or parameter.IsReadOnly:
        return False

    try:
        if parameter.StorageType == StorageType.Double:
            parameter.Set(float(value))
            return True
        if parameter.StorageType == StorageType.Integer:
            parameter.Set(int(value))
            return True
        if parameter.StorageType == StorageType.String:
            parameter.Set(safe_string(value))
            return True
    except Exception:
        return False

    return False


def get_room_label(room_or_space):
    name = None
    number = None
    try:
        name = room_or_space.LookupParameter("Name").AsString()
    except Exception:
        try:
            name = room_or_space.Name
        except Exception:
            name = None

    try:
        number = room_or_space.LookupParameter("Number").AsString()
    except Exception:
        number = None

    if number and name:
        return "{0} - {1}".format(number, name)
    if name:
        return name
    return "ElementId {0}".format(room_or_space.Id.IntegerValue)


def get_room_name_parameter_value(room_or_space):
    name_value, name_param = read_string_parameter(
        room_or_space,
        ["room name", "Room Name", "Name"],
    )
    if name_value:
        return name_value, name_param

    try:
        builtin_name = room_or_space.get_Parameter(BuiltInParameter.ROOM_NAME)
        builtin_value = safe_string(get_parameter_value(builtin_name)).strip()
        if builtin_value:
            return builtin_value, "BuiltInParameter.ROOM_NAME"
    except Exception:
        pass

    try:
        fallback_name = safe_string(room_or_space.Name).strip()
        if fallback_name:
            return fallback_name, "Name property"
    except Exception:
        pass

    return None, None


def get_room_level_value(room_or_space):
    try:
        level_id = room_or_space.LevelId
        if level_id and level_id != ElementId.InvalidElementId:
            level = doc.GetElement(level_id)
            if level is not None:
                level_name = safe_string(getattr(level, "Name", None)).strip()
                if level_name:
                    return level_name, "LevelId"
    except Exception:
        pass

    level_value, level_param = read_string_parameter(
        room_or_space,
        ["Level", "Reference Level", "레벨"],
    )
    if level_value:
        return level_value, level_param

    return None, None


def sanitize_file_name(text):
    invalid_chars = '<>:"/\\|?*'
    result = safe_string(text)
    for invalid_char in invalid_chars:
        result = result.replace(invalid_char, "_")
    return result.strip() or "output"


def export_room_results_to_csv(document, fixture_label, room_results):
    doc_title = "RevitModel"
    try:
        doc_title = document.Title
    except Exception:
        pass

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    default_file_name = "{0}_조도계산_{1}.csv".format(
        sanitize_file_name(doc_title),
        timestamp,
    )
    _, input_file_path = normalize_export_input_path(EXPORT_PATH_INPUT)
    field_names = [csv_header for _, csv_header in CSV_FIELD_MAPPINGS]

    last_error = None

    for export_dir in get_export_directories():
        try:
            if not os.path.isdir(export_dir):
                os.makedirs(export_dir)

            file_path = input_file_path if input_file_path and os.path.dirname(input_file_path) == export_dir else os.path.join(export_dir, default_file_name)

            with open(file_path, "w", newline="", encoding="utf-8-sig") as csv_file:
                writer = csv.DictWriter(csv_file, fieldnames=field_names)
                writer.writerow(
                    {
                        csv_header: CSV_GROUP_LABELS.get(source_key, "")
                        for source_key, csv_header in CSV_FIELD_MAPPINGS
                    }
                )
                writer.writeheader()

                for room_result in room_results:
                    used_parameters = room_result.get("usedParameters", {})
                    row_values = {
                        "roomId": room_result.get("roomId"),
                        "room": room_result.get("room"),
                        "roomNameParamValue": room_result.get("roomNameParamValue"),
                        "levelName": room_result.get("levelName"),
                        "status": room_result.get("status"),
                        "message": room_result.get("message", ""),
                        "area_m2": room_result.get("area_m2"),
                        "length_m": room_result.get("length_m"),
                        "width_m": room_result.get("width_m"),
                        "effectiveHeight_m": room_result.get("effectiveHeight_m"),
                        "roomIndex": room_result.get("roomIndex"),
                        "requiredLux": room_result.get("requiredLux"),
                        "utilizationFactor": room_result.get("utilizationFactor"),
                        "maintenanceFactor": room_result.get("maintenanceFactor"),
                        "reflectance": room_result.get("reflectance"),
                        "wallReflectance": room_result.get("wallReflectance"),
                        "ceilingReflectance": room_result.get("ceilingReflectance"),
                        "floorReflectance": room_result.get("floorReflectance"),
                        "fixture": fixture_label,
                        "fixtureFlux_lm": room_result.get("fixtureFlux_lm"),
                        "rawRequiredCount": room_result.get("rawRequiredCount"),
                        "requiredCount": room_result.get("requiredCount"),
                        "calculatedIlluminance": room_result.get("calculatedIlluminance"),
                        "requiredLuxParam": used_parameters.get("requiredLux"),
                        "utilizationParam": used_parameters.get("utilization"),
                        "maintenanceParam": used_parameters.get("maintenance"),
                        "reflectanceParam": used_parameters.get("reflectance"),
                        "wallReflectanceParam": used_parameters.get("wallReflectance"),
                        "ceilingReflectanceParam": used_parameters.get("ceilingReflectance"),
                        "floorReflectanceParam": used_parameters.get("floorReflectance"),
                        "fixtureFluxParam": used_parameters.get("fixtureFlux"),
                    }
                    writer.writerow(
                        {
                            csv_header: row_values.get(source_key)
                            for source_key, csv_header in CSV_FIELD_MAPPINGS
                        }
                    )

            return file_path, None
        except Exception as export_exception:
            last_error = "{0}: {1}".format(export_dir, export_exception)

    return None, last_error


if not RUN_GRAPH:
    OUT = {
        "status": "idle",
        "message": "재실행 버튼을 True로 바꾸면 현재 선택한 Room/Space와 Lighting Fixture로 계산하고 CSV까지 저장합니다.",
        "requiredRoomParameters": [
            ROOM_REQUIRED_LUX_PARAM,
            ROOM_UTILIZATION_PARAM,
            ROOM_MAINTENANCE_PARAM,
            ROOM_REFLECTANCE_PARAM,
            ROOM_EFFECTIVE_HEIGHT_PARAM,
        ],
        "fixtureFluxParameter": FIXTURE_FLUX_PARAM,
        "resultParameters": [
            ROOM_INDEX_PARAM,
            ROOM_RESULT_COUNT_PARAM,
            ROOM_RESULT_FLUX_PARAM,
            ROOM_RESULT_FIXTURE_PARAM,
        ],
        "exportFolders": get_export_directories(),
        "exportPathInput": EXPORT_PATH_INPUT,
    }
else:
    selected_elements = get_selected_elements()
    selected_rooms = [element for element in selected_elements if is_room_or_space(element)]
    selected_fixtures = [element for element in selected_elements if is_lighting_fixture(element)]

    if not selected_rooms:
        OUT = {
            "status": "error",
            "message": "계산할 Room 또는 Space를 먼저 선택하세요.",
            "selectedCount": len(selected_elements),
        }
    elif not selected_fixtures:
        OUT = {
            "status": "error",
            "message": "기준이 될 Lighting Fixture 인스턴스 1개를 함께 선택하세요.",
            "selectedRooms": len(selected_rooms),
        }
    else:
        fixture = selected_fixtures[0]
        fixture_flux, fixture_flux_param_name = read_fixture_flux(fixture)
        fixture_label = get_fixture_label(fixture)

        if fixture_flux is None or fixture_flux <= 0:
            OUT = {
                "status": "error",
                "message": "선택한 Lighting Fixture에서 광속 값을 읽지 못했습니다.",
                "fixture": fixture_label,
                "searchedParameterNames": COMMON_FLUX_PARAM_NAMES,
            }
        else:
            room_results = []
            writable_rooms = []

            for room in selected_rooms:
                area = read_room_area(room)
                length_m, width_m = read_room_plan_dimensions(room)
                room_name_value, room_name_param = get_room_name_parameter_value(room)
                level_name, level_param = get_room_level_value(room)
                effective_height_m, effective_height_param = read_numeric_parameter(room, ROOM_EFFECTIVE_HEIGHT_PARAM)
                calculated_room_index = calculate_room_index(area, length_m, width_m, effective_height_m)
                calculated_utilization = get_utilization_from_room_index(calculated_room_index)
                required_lux, required_lux_param = read_numeric_parameter(room, ROOM_REQUIRED_LUX_PARAM)
                utilization, utilization_param = read_numeric_parameter(room, ROOM_UTILIZATION_PARAM)
                maintenance, maintenance_param = read_numeric_parameter(room, ROOM_MAINTENANCE_PARAM)
                reflectance, reflectance_param = read_reflectance_parameter(room)
                surface_reflectances = read_surface_reflectance_parameters(room)

                if calculated_utilization is not None:
                    utilization = calculated_utilization
                    utilization_param = ROOM_UTILIZATION_PARAM + " (auto)"

                room_result = {
                    "roomId": room.Id.IntegerValue,
                    "room": get_room_label(room),
                    "roomNameParamValue": room_name_value,
                    "levelName": level_name,
                    "area_m2": round(area, 4) if area is not None else None,
                    "length_m": round(length_m, 4) if length_m is not None else None,
                    "width_m": round(width_m, 4) if width_m is not None else None,
                    "effectiveHeight_m": round(effective_height_m, 4) if effective_height_m is not None else None,
                    "roomIndex": round(calculated_room_index, 4) if calculated_room_index is not None else None,
                    "requiredLux": required_lux,
                    "utilizationFactor": utilization,
                    "maintenanceFactor": maintenance,
                    "reflectance": reflectance,
                    "wallReflectance": surface_reflectances["wallReflectance"],
                    "ceilingReflectance": surface_reflectances["ceilingReflectance"],
                    "floorReflectance": surface_reflectances["floorReflectance"],
                    "fixtureFlux_lm": fixture_flux,
                    "fixture": fixture_label,
                    "status": "ok",
                }

                missing = []
                if area is None or area <= 0:
                    missing.append("면적(Area)")
                if required_lux is None or required_lux <= 0:
                    missing.append(ROOM_REQUIRED_LUX_PARAM)
                if utilization is None or utilization <= 0:
                    missing.append(ROOM_UTILIZATION_PARAM)
                if maintenance is None or maintenance <= 0:
                    missing.append(ROOM_MAINTENANCE_PARAM)
                if effective_height_m is None or effective_height_m <= 0:
                    missing.append(ROOM_EFFECTIVE_HEIGHT_PARAM)

                if missing:
                    room_result["status"] = "skipped"
                    room_result["message"] = "필수 값 누락: {0}".format(", ".join(missing))
                    room_results.append(room_result)
                    continue

                if reflectance is None:
                    room_result["message"] = "반사율 미추출: Space/Room에 조도_반사율 또는 반사율 파라미터 필요"

                raw_count = (required_lux * area) / (fixture_flux * utilization * maintenance)
                required_count = int(math.ceil(raw_count))
                calculated_illuminance = (fixture_flux * utilization * maintenance * required_count) / area

                room_result["rawRequiredCount"] = round(raw_count, 4)
                room_result["requiredCount"] = required_count
                room_result["calculatedIlluminance"] = round(calculated_illuminance, 4)
                room_result["usedParameters"] = {
                    "requiredLux": required_lux_param,
                    "utilization": utilization_param,
                    "maintenance": maintenance_param,
                    "reflectance": reflectance_param,
                    "wallReflectance": surface_reflectances["wallReflectanceParam"],
                    "ceilingReflectance": surface_reflectances["ceilingReflectanceParam"],
                    "floorReflectance": surface_reflectances["floorReflectanceParam"],
                    "fixtureFlux": fixture_flux_param_name,
                    "effectiveHeight": effective_height_param,
                    "roomIndex": ROOM_INDEX_PARAM,
                    "roomName": room_name_param,
                    "levelName": level_param,
                }
                room_results.append(room_result)
                writable_rooms.append((room, room_result))

            write_log = []
            if writable_rooms:
                TransactionManager.Instance.EnsureInTransaction(doc)
                try:
                    for room, room_result in writable_rooms:
                        write_result = {
                            "roomId": room.Id.IntegerValue,
                            "room": room_result["room"],
                            "writes": {},
                        }

                        count_param = get_parameter(room, ROOM_RESULT_COUNT_PARAM)
                        flux_param = get_parameter(room, ROOM_RESULT_FLUX_PARAM)
                        fixture_param = get_parameter(room, ROOM_RESULT_FIXTURE_PARAM)
                        room_index_param = get_parameter(room, ROOM_INDEX_PARAM)
                        utilization_param = get_parameter(room, ROOM_UTILIZATION_PARAM)

                        write_result["writes"][ROOM_RESULT_COUNT_PARAM] = set_parameter_value(
                            count_param,
                            room_result["requiredCount"],
                        )
                        write_result["writes"][ROOM_RESULT_FLUX_PARAM] = set_parameter_value(
                            flux_param,
                            fixture_flux,
                        )
                        write_result["writes"][ROOM_RESULT_FIXTURE_PARAM] = set_parameter_value(
                            fixture_param,
                            fixture_label,
                        )
                        write_result["writes"][ROOM_INDEX_PARAM] = set_parameter_value(
                            room_index_param,
                            room_result.get("roomIndex"),
                        )
                        write_result["writes"][ROOM_UTILIZATION_PARAM] = set_parameter_value(
                            utilization_param,
                            room_result.get("utilizationFactor"),
                        )

                        write_log.append(write_result)
                finally:
                    TransactionManager.Instance.TransactionTaskDone()

            export_path, export_error = export_room_results_to_csv(doc, fixture_label, room_results)

            OUT = {
                "status": "ok",
                "message": "선택한 Lighting Fixture 기준으로 Room/Space 필요 조명 개수를 계산했습니다.",
                "selectedRoomCount": len(selected_rooms),
                "selectedFixtureCount": len(selected_fixtures),
                "fixture": {
                    "label": fixture_label,
                    "flux_lm": fixture_flux,
                    "fluxParameter": fixture_flux_param_name,
                },
                "formula": "CEILING((요구조도 * 면적) / (광속 * 조명율 * 보수율))",
                "note": "반사율은 결과표 기록용으로 읽고, 현재 계산식에는 직접 사용하지 않습니다.",
                "roomResults": room_results,
                "writeLog": write_log,
                "exportFolders": get_export_directories(),
                "exportPathInput": EXPORT_PATH_INPUT,
                "exportCsvPath": export_path,
                "exportCsvError": export_error,
            }
