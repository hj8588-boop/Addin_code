import clr
import re
import System

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import BuiltInCategory, ElementId, StorageType

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument

elements_input = IN[0] if len(IN) > 0 else []
parameter_names_input = IN[1] if len(IN) > 1 else []
values_input = IN[2] if len(IN) > 2 else []
toggles_input = IN[3] if len(IN) > 3 else []
level8_family_rules_input = IN[4] if len(IN) > 4 else None

LEVEL8_FAMILY_RULE_CATEGORIES = [
    "ostelectricalfixtures",
    "ostlightingfixtures",
    "ostelectricalequipment",
    "ostcabletrayfitting",
    "ostcabletray",
    "ostconduit",
    "ostconduitfitting",
]


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


def normalize_key(value):
    text = normalize_text(value).lower()
    return re.sub(r"[\s_\-]+", "", text)


def to_bool(value):
    if isinstance(value, bool):
        return value
    text = normalize_text(value).lower()
    return text in ["true", "1", "yes", "y"]


def split_category_rules(text):
    separators = ["\r\n", "\n", "|", ";"]
    parts = [text]
    for separator in separators:
        next_parts = []
        for part in parts:
            next_parts.extend(part.split(separator))
        parts = next_parts
    return [part.strip() for part in parts if part and part.strip()]


def parse_category_value_map(value):
    text = normalize_text(value)
    if "=" not in text:
        return None

    mapping = {}
    for part in split_category_rules(text):
        if "=" not in part:
            return None
        key, mapped_value = part.split("=", 1)
        normalized = normalize_key(key)
        if not normalized:
            return None
        mapping[normalized] = normalize_text(mapped_value)

    return mapping if mapping else None


def parse_family_value_map(value):
    text = normalize_text(value)
    if "::" not in text or "=" not in text:
        return None

    mapping = {}
    for part in split_category_rules(text):
        if "::" not in part or "=" not in part:
            return None

        scope, mapped_value = part.split("=", 1)
        category_name, family_name = scope.split("::", 1)
        category_key = normalize_key(category_name)
        family_key = normalize_key(family_name)

        if not category_key or not family_key:
            return None

        if category_key not in mapping:
            mapping[category_key] = {}

        mapping[category_key][family_key] = normalize_text(mapped_value)

    return mapping if mapping else None


def parse_level8_category_family_rules(value, default_category_key):
    text = normalize_text(value)
    if not text or "=" not in text:
        return None

    mapping = {default_category_key: {}}
    for part in split_category_rules(text):
        if "=" not in part:
            return None

        scope, mapped_value = part.split("=", 1)
        scope = normalize_text(scope)
        category_name = default_category_key
        family_name = scope

        if "::" in scope:
            category_name, family_name = scope.split("::", 1)
            category_name = normalize_key(category_name)
            family_name = normalize_key(family_name)
        else:
            family_name = normalize_key(family_name)

        if not family_name:
            return None

        if not category_name:
            category_name = default_category_key

        if category_name not in mapping:
            mapping[category_name] = {}

        mapping[category_name][family_name] = normalize_text(mapped_value)

    return mapping


def build_level8_family_rule_map(value):
    if isinstance(value, (list, tuple)):
        combined = {}
        for index, raw_item in enumerate(value):
            category_key = (
                LEVEL8_FAMILY_RULE_CATEGORIES[index]
                if index < len(LEVEL8_FAMILY_RULE_CATEGORIES)
                else "ostelectricalequipment"
            )
            parsed = parse_level8_category_family_rules(raw_item, category_key)
            if parsed is None:
                continue
            for parsed_category_key, rules in parsed.items():
                if parsed_category_key not in combined:
                    combined[parsed_category_key] = {}
                combined[parsed_category_key].update(rules)
        return combined if combined else None

    return parse_level8_category_family_rules(value, "ostelectricalequipment")


def get_category_keys(element):
    keys = []
    category = getattr(element, "Category", None)
    if category is None:
        return keys

    keys.append(normalize_key(category.Name))

    try:
        builtin_value = System.Enum.ToObject(BuiltInCategory, category.Id.IntegerValue)
        builtin_name = System.Enum.GetName(BuiltInCategory, builtin_value)
        if builtin_name:
            keys.append(normalize_key(builtin_name))
    except Exception:
        pass

    return [key for key in keys if key]


def get_family_keys(element):
    keys = []

    symbol = getattr(element, "Symbol", None)
    if symbol is not None:
        family = getattr(symbol, "Family", None)
        if family is not None and getattr(family, "Name", None):
            keys.append(normalize_key(family.Name))
        if getattr(symbol, "FamilyName", None):
            keys.append(normalize_key(symbol.FamilyName))

    try:
        element_type = doc.GetElement(element.GetTypeId())
        if element_type is not None:
            family_name = getattr(element_type, "FamilyName", None)
            if family_name:
                keys.append(normalize_key(family_name))
    except Exception:
        pass

    seen = set()
    unique_keys = []
    for key in keys:
        if key and key not in seen:
            seen.add(key)
            unique_keys.append(key)

    return unique_keys


def resolve_value_for_element(raw_value, element):
    family_mapping = parse_family_value_map(raw_value)
    if family_mapping is not None:
        category_keys = get_category_keys(element)
        family_keys = get_family_keys(element)

        for category_key in category_keys:
            category_rules = family_mapping.get(category_key)
            if not category_rules:
                continue

            for family_key in family_keys:
                if family_key in category_rules:
                    return True, category_rules[family_key], ""

            for fallback_key in ["default", "all", "*"]:
                if fallback_key in category_rules:
                    return True, category_rules[fallback_key], ""

        category = getattr(element, "Category", None)
        category_name = category.Name if category is not None else "Unknown"
        return False, None, "No family mapping found for '{0}'.".format(category_name)

    mapping = parse_category_value_map(raw_value)
    if mapping is None:
        return True, raw_value, ""

    for key in get_category_keys(element):
        if key in mapping:
            return True, mapping[key], ""

    for fallback_key in ["default", "all", "*"]:
        if fallback_key in mapping:
            return True, mapping[fallback_key], ""

    category = getattr(element, "Category", None)
    category_name = category.Name if category is not None else "Unknown"
    return False, None, "No category mapping found for '{0}'.".format(category_name)


def resolve_level8_value_for_element(raw_value, family_rules_value, element):
    category_keys = get_category_keys(element)
    family_rules = build_level8_family_rule_map(family_rules_value)

    if family_rules is not None:
        family_keys = get_family_keys(element)

        for category_key in category_keys:
            category_rules = family_rules.get(category_key, {})
            if not category_rules:
                continue

            for family_key in family_keys:
                if family_key in category_rules:
                    return True, category_rules[family_key], ""

            for fallback_key in ["default", "all", "*"]:
                if fallback_key in category_rules:
                    return True, category_rules[fallback_key], ""

    return resolve_value_for_element(raw_value, element)


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


def get_parameter_text(element, parameter_name):
    try:
        parameter = element.LookupParameter(parameter_name)
    except Exception:
        parameter = None

    if parameter is None:
        return ""

    try:
        if parameter.StorageType == StorageType.String:
            return normalize_text(parameter.AsString())
        return normalize_text(parameter.AsValueString() or parameter.AsString())
    except Exception:
        return ""


def resolve_wbs_code_value(element):
    level7 = get_parameter_text(element, "15.A3_Level7")
    level8 = get_parameter_text(element, "16.A3_Level8")
    return "{0}{1}".format(level7, level8)


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
        raw_value = values[index]
        is_enabled = to_bool(toggles[index])

        if not parameter_name:
            skipped.append({"index": index, "reason": "Empty parameter name."})
            continue

        if not is_enabled:
            skipped.append({"parameter": parameter_name, "reason": "Toggle is off."})
            continue

        success_count = 0
        failure_count = 0
        row_messages = []

        for element in elements:
            if parameter_name == "17.A3_WBS Code":
                ok = True
                resolved_value = resolve_wbs_code_value(element)
                resolve_message = ""
            elif parameter_name == "16.A3_Level8":
                ok, resolved_value, resolve_message = resolve_level8_value_for_element(
                    raw_value, level8_family_rules_input, element
                )
            else:
                ok, resolved_value, resolve_message = resolve_value_for_element(raw_value, element)
            if not ok:
                failure_count += 1
                row_messages.append(resolve_message)
                continue

            parameter = None
            try:
                parameter = element.LookupParameter(parameter_name)
            except Exception:
                parameter = None

            ok, message = set_parameter_value(parameter, resolved_value)
            if ok:
                success_count += 1
            else:
                failure_count += 1
                if message:
                    row_messages.append(message)

        if success_count > 0:
            applied.append({
                "parameter": parameter_name,
                "value": raw_value,
                "successCount": success_count,
                "failureCount": failure_count,
                "messages": row_messages[:10],
            })
        else:
            failed.append({
                "parameter": parameter_name,
                "value": raw_value,
                "reason": "No elements were updated.",
                "messages": row_messages[:10],
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
