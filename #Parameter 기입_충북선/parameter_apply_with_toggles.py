import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import ElementId, StorageType

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument

elements_input = IN[0] if len(IN) > 0 else []
parameter_names_input = IN[1] if len(IN) > 1 else []
values_input = IN[2] if len(IN) > 2 else []
toggles_input = IN[3] if len(IN) > 3 else []


def ensure_list(value):
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


def flatten_list(values):
    result = []
    for item in ensure_list(values):
        if isinstance(item, (list, tuple)):
            result.extend(flatten_list(item))
        elif hasattr(item, "GetType") and "List" in str(item.GetType()):
            result.extend(flatten_list(list(item)))
        else:
            result.append(item)
    return result


def unwrap_element(value):
    if value is None:
        return None
    if hasattr(value, "InternalElement") and value.InternalElement is not None:
        return value.InternalElement
    return value


def normalize_text(value):
    if value is None:
        return ""
    return str(value).strip()


def to_bool(value):
    if isinstance(value, bool):
        return value
    text = normalize_text(value).lower()
    return text in ["true", "1", "yes", "y"]


def set_parameter_value(parameter, value):
    if parameter is None or parameter.IsReadOnly:
        return False, "Parameter missing or read-only."

    try:
        storage_type = parameter.StorageType

        if storage_type == StorageType.String:
            parameter.Set("" if value is None else str(value))
            return True, ""

        if storage_type == StorageType.Integer:
            if isinstance(value, bool):
                parameter.Set(1 if value else 0)
                return True, ""
            parameter.Set(int(value))
            return True, ""

        if storage_type == StorageType.Double:
            parameter.Set(float(value))
            return True, ""

        if storage_type == StorageType.ElementId:
            if hasattr(value, "IntegerValue"):
                parameter.Set(value)
                return True, ""
            parameter.Set(ElementId(int(value)))
            return True, ""

        return False, "Unsupported storage type."
    except Exception as exc:
        return False, str(exc)


elements = []
for item in flatten_list(elements_input):
    element = unwrap_element(item)
    if element is not None:
        elements.append(element)

parameter_names = flatten_list(parameter_names_input)
values = flatten_list(values_input)
toggles = flatten_list(toggles_input)

if not elements:
    raise Exception("No target elements were found.")

pair_count = min(len(parameter_names), len(values), len(toggles))
if pair_count == 0:
    raise Exception("No parameter rows were supplied.")

applied = []
skipped = []
failed = []

TransactionManager.Instance.EnsureInTransaction(doc)

try:
    for index in range(pair_count):
        parameter_name = normalize_text(parameter_names[index])
        value = values[index]
        is_enabled = to_bool(toggles[index])

        if not parameter_name:
            skipped.append({"index": index, "reason": "Empty parameter name."})
            continue

        if not is_enabled:
            skipped.append({"parameter": parameter_name, "reason": "Toggle is off."})
            continue

        success_count = 0
        failure_count = 0

        for element in elements:
            parameter = None
            try:
                parameter = element.LookupParameter(parameter_name)
            except Exception:
                parameter = None

            ok, message = set_parameter_value(parameter, value)
            if ok:
                success_count += 1
            else:
                failure_count += 1

        if success_count > 0:
            applied.append({
                "parameter": parameter_name,
                "value": value,
                "successCount": success_count,
                "failureCount": failure_count,
            })
        else:
            failed.append({
                "parameter": parameter_name,
                "value": value,
                "reason": "No elements were updated.",
            })
finally:
    TransactionManager.Instance.TransactionTaskDone()

OUT = {
    "elementCount": len(elements),
    "parameterRowCount": pair_count,
    "applied": applied,
    "skipped": skipped,
    "failed": failed,
}
