import clr
import System
import traceback

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    BuiltInCategory,
    BuiltInParameter,
    CategoryType,
    ElementId,
    FilteredElementCollector,
    StorageType,
)

try:
    from Autodesk.Revit.DB import ParameterTypeId
except Exception:
    ParameterTypeId = None

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument

category_inputs = IN[0] if len(IN) > 0 else None
ifc_class_inputs = IN[1] if len(IN) > 1 else None
ifc_standard_inputs = IN[2] if len(IN) > 2 else None
include_element_types = True if len(IN) < 4 or IN[3] is None else bool(IN[3])


def normalize_text(value):
    if value is None:
        return ""
    return str(value).strip()


def to_sequence(value):
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        return list(value)

    if hasattr(value, "GetType") and "List" in str(value.GetType()):
        try:
            return list(value)
        except Exception:
            pass

    return [value]


def unwrap_revit_category(value):
    if value is None:
        return None

    if hasattr(value, "InternalCategory") and value.InternalCategory is not None:
        return value.InternalCategory

    if hasattr(value, "Id") and hasattr(value, "Name") and hasattr(value, "AllowsBoundParameters"):
        return value

    return None


def get_category_by_builtin_name(document, category_name):
    safe_name = normalize_text(category_name)
    if not safe_name:
        return None

    builtin_name = safe_name if safe_name.startswith("OST_") else "OST_{0}".format(safe_name)

    try:
        built_in_category = System.Enum.Parse(BuiltInCategory, builtin_name, True)
        category = document.Settings.Categories.get_Item(built_in_category)
        if category is not None:
            return category
    except Exception:
        pass

    for category in document.Settings.Categories:
        try:
            if category.Name == safe_name:
                return category
        except Exception:
            continue

    return None


def resolve_category(document, value):
    category = unwrap_revit_category(value)
    if category is None:
        category = get_category_by_builtin_name(document, value)
    if category is None:
        raise Exception("Selected category could not be resolved: {0}".format(value))

    if category.CategoryType != CategoryType.Model:
        raise Exception("Only model categories are supported: {0}".format(category.Name))

    return category


def resolve_categories(document, values):
    resolved = []
    seen_ids = set()

    for value in to_sequence(values):
        category = resolve_category(document, value)
        category_id = category.Id.IntegerValue
        if category_id in seen_ids:
            continue
        seen_ids.add(category_id)
        resolved.append(category)

    if not resolved:
        raise Exception("Please provide at least one Revit category.")

    return resolved


def resolve_text_list(values):
    result = []
    for value in to_sequence(values):
        result.append(normalize_text(value))
    return result


def validate_parallel_lists(categories, ifc_classes, ifc_standards):
    category_count = len(categories)
    if category_count != len(ifc_classes):
        raise Exception("Category count and IFC Class count do not match.")
    if category_count != len(ifc_standards):
        raise Exception("Category count and IFC Standard count do not match.")

    for index in range(category_count):
        if not ifc_classes[index]:
            raise Exception("IFC Class is empty at index {0}.".format(index))
        if not ifc_standards[index]:
            raise Exception("IFC Standard is empty at index {0}.".format(index))


def forge_type_id_equals(left, right):
    if left is None or right is None:
        return False

    try:
        return left.TypeId == right.TypeId
    except Exception:
        pass

    try:
        return left == right
    except Exception:
        return False


def get_parameter_by_data_type(element, target_type_id):
    if ParameterTypeId is None or target_type_id is None:
        return None

    for parameter in element.Parameters:
        try:
            data_type = parameter.Definition.GetDataType()
            if forge_type_id_equals(data_type, target_type_id):
                return parameter
        except Exception:
            continue

    return None


def get_parameter_by_names(element, names):
    for name in names:
        safe_name = normalize_text(name)
        if not safe_name:
            continue

        try:
            parameter = element.LookupParameter(safe_name)
            if parameter is not None:
                return parameter
        except Exception:
            continue

    return None


def get_parameter_by_builtin(element, built_in_parameter):
    try:
        return element.get_Parameter(built_in_parameter)
    except Exception:
        return None


def get_element_type(element):
    try:
        type_id = element.GetTypeId()
        if type_id is None or type_id == ElementId.InvalidElementId:
            return None
        return doc.GetElement(type_id)
    except Exception:
        return None


def get_ifc_class_parameter(element):
    for built_in_parameter in [
        BuiltInParameter.IFC_EXPORT_ELEMENT_AS,
        BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE_AS,
    ]:
        parameter = get_parameter_by_builtin(element, built_in_parameter)
        if parameter is not None:
            return parameter

    if ParameterTypeId is not None:
        for property_name in [
            "IfcExportAs",
            "IfcExportTypeAs",
            "IfcExportElementAs",
        ]:
            if hasattr(ParameterTypeId, property_name):
                parameter = get_parameter_by_data_type(element, getattr(ParameterTypeId, property_name))
                if parameter is not None:
                    return parameter

    return get_parameter_by_names(
        element,
        [
            "IFC Export As",
            "Export to IFC As",
            "Export Type to IFC As",
            "IfcExportAs",
            "IfcExportTypeAs",
            "IFC Class",
        ],
    )


def get_ifc_standard_parameter(element):
    for built_in_parameter in [
        BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE,
        BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE_TYPE,
        BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE,
    ]:
        parameter = get_parameter_by_builtin(element, built_in_parameter)
        if parameter is not None:
            return parameter

    if ParameterTypeId is not None:
        for property_name in [
            "IfcPredefinedType",
            "IfcExportPredefinedtypeType",
            "IfcExportElementType",
            "IfcExportPredefinedType",
            "IfcExportPredefinedtype",
            "IfcExportType",
        ]:
            if hasattr(ParameterTypeId, property_name):
                parameter = get_parameter_by_data_type(element, getattr(ParameterTypeId, property_name))
                if parameter is not None:
                    return parameter

    return get_parameter_by_names(
        element,
        [
            "IFC Predefined Type",
            "Type IFC Predefined Type",
            "Export Type to IFC",
            "Export Type to IFC As",
            "IfcExportType",
            "IFC Standard",
        ],
    )


def set_parameter_value(parameter, value):
    if parameter is None:
        return False, "missing"

    if parameter.IsReadOnly:
        return False, "readonly"

    try:
        if parameter.Set(value):
            return True, "set"
    except Exception:
        pass

    try:
        parameter.SetValueString(value)
        return True, "set"
    except Exception:
        return False, "failed"


def get_writable_parameter(element, getter):
    parameter = getter(element)
    if parameter is not None:
        return parameter, "element"

    element_type = get_element_type(element)
    if element_type is not None:
        parameter = getter(element_type)
        if parameter is not None:
            return parameter, "type"

    return None, "missing"


def collect_instances(document, category):
    collector = (
        FilteredElementCollector(document)
        .OfCategoryId(category.Id)
        .WhereElementIsNotElementType()
    )
    return list(collector)


def collect_types(document, category):
    collector = (
        FilteredElementCollector(document)
        .OfCategoryId(category.Id)
        .WhereElementIsElementType()
    )
    return list(collector)


def parameter_current_value(parameter):
    if parameter is None:
        return None

    try:
        if parameter.StorageType == StorageType.String:
            return parameter.AsString()
    except Exception:
        pass

    try:
        return parameter.AsValueString()
    except Exception:
        return None


def try_apply_values(target, ifc_class_value, ifc_standard_value):
    class_parameter = get_ifc_class_parameter(target)
    standard_parameter = get_ifc_standard_parameter(target)

    class_ok = False
    class_status = "missing"
    standard_ok = False
    standard_status = "missing"

    if class_parameter is not None:
        if normalize_text(parameter_current_value(class_parameter)) == normalize_text(ifc_class_value):
            class_ok, class_status = True, "unchanged"
        else:
            class_ok, class_status = set_parameter_value(class_parameter, ifc_class_value)

    if standard_parameter is not None:
        if normalize_text(parameter_current_value(standard_parameter)) == normalize_text(ifc_standard_value):
            standard_ok, standard_status = True, "unchanged"
        else:
            standard_ok, standard_status = set_parameter_value(standard_parameter, ifc_standard_value)

    return {
        "classParameter": class_parameter,
        "standardParameter": standard_parameter,
        "classOk": class_ok,
        "classStatus": class_status,
        "standardOk": standard_ok,
        "standardStatus": standard_status,
    }


def ensure_counter(result, key):
    if key not in result:
        result[key] = 0
    result[key] += 1


def record_status(result, class_status, standard_status):
    if class_status == "missing":
        result["missingClassParameter"] += 1
    elif class_status == "readonly":
        ensure_counter(result, "readonlyClassCount")
    elif class_status == "failed":
        ensure_counter(result, "failedClassCount")
    elif class_status == "unchanged":
        ensure_counter(result, "unchangedClass")

    if standard_status == "missing":
        result["missingStandardParameter"] += 1
    elif standard_status == "readonly":
        ensure_counter(result, "readonlyStandardCount")
    elif standard_status == "failed":
        ensure_counter(result, "failedStandardCount")
    elif standard_status == "unchanged":
        ensure_counter(result, "unchangedStandard")

try:
    categories = resolve_categories(doc, category_inputs)
    ifc_classes = resolve_text_list(ifc_class_inputs)
    ifc_standards = resolve_text_list(ifc_standard_inputs)
    validate_parallel_lists(categories, ifc_classes, ifc_standards)

    summary = []
    missing_parameter_samples = []

    TransactionManager.Instance.EnsureInTransaction(doc)

    try:
        for index, category in enumerate(categories):
            ifc_class_value = ifc_classes[index]
            ifc_standard_value = ifc_standards[index]
            instances = collect_instances(doc, category)
            types = collect_types(doc, category) if include_element_types else []
            processed_type_ids = set()

            category_result = {
                "category": category.Name,
                "ifcClass": ifc_class_value,
                "ifcStandard": ifc_standard_value,
                "instanceCount": len(instances),
                "typeCount": len(types),
                "targetCount": len(instances) + len(types),
                "updatedClass": 0,
                "updatedStandard": 0,
                "missingClassParameter": 0,
                "missingStandardParameter": 0,
                "readonlyClassCount": 0,
                "readonlyStandardCount": 0,
                "failedClassCount": 0,
                "failedStandardCount": 0,
                "unchangedClass": 0,
                "unchangedStandard": 0,
            }

            for element in instances:
                apply_result = try_apply_values(element, ifc_class_value, ifc_standard_value)

                if (
                    apply_result["classParameter"] is None
                    and apply_result["standardParameter"] is None
                    and len(missing_parameter_samples) < 10
                ):
                    missing_parameter_samples.append(
                        {
                            "category": category.Name,
                            "elementId": element.Id.IntegerValue,
                            "elementName": getattr(element, "Name", ""),
                        }
                    )

                if apply_result["classOk"]:
                    category_result["updatedClass"] += 1
                if apply_result["standardOk"]:
                    category_result["updatedStandard"] += 1

                record_status(
                    category_result,
                    apply_result["classStatus"],
                    apply_result["standardStatus"],
                )

                if apply_result["classParameter"] is not None or apply_result["standardParameter"] is not None:
                    continue

                element_type = get_element_type(element)
                if element_type is None:
                    continue

                type_id_value = element_type.Id.IntegerValue
                if type_id_value in processed_type_ids:
                    continue

                processed_type_ids.add(type_id_value)
                type_result = try_apply_values(element_type, ifc_class_value, ifc_standard_value)

                if type_result["classOk"]:
                    ensure_counter(category_result, "updatedClassOnType")
                if type_result["standardOk"]:
                    ensure_counter(category_result, "updatedStandardOnType")

                record_status(
                    category_result,
                    type_result["classStatus"],
                    type_result["standardStatus"],
                )

            if include_element_types:
                for element_type in types:
                    type_id_value = element_type.Id.IntegerValue
                    if type_id_value in processed_type_ids:
                        continue

                    processed_type_ids.add(type_id_value)
                    type_result = try_apply_values(element_type, ifc_class_value, ifc_standard_value)

                    if (
                        type_result["classParameter"] is None
                        and type_result["standardParameter"] is None
                        and len(missing_parameter_samples) < 10
                    ):
                        missing_parameter_samples.append(
                            {
                                "category": category.Name,
                                "elementId": element_type.Id.IntegerValue,
                                "elementName": getattr(element_type, "Name", ""),
                            }
                        )

                    if type_result["classOk"]:
                        ensure_counter(category_result, "updatedClassOnType")
                    if type_result["standardOk"]:
                        ensure_counter(category_result, "updatedStandardOnType")

                    record_status(
                        category_result,
                        type_result["classStatus"],
                        type_result["standardStatus"],
                    )

            summary.append(category_result)
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    OUT = {
        "ok": True,
        "includeElementTypes": include_element_types,
        "categoryCount": len(categories),
        "summary": summary,
        "missingParameterSamples": missing_parameter_samples,
        "notes": [
            "Lists must be aligned by index: Category[i], IFC Class[i], IFC Standard[i].",
            "IFC Standard input is written to IFC Predefined Type when available.",
        ],
    }
except Exception as ex:
    OUT = {
        "ok": False,
        "error": str(ex),
        "traceback": traceback.format_exc(),
    }
