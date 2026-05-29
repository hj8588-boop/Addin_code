import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import FamilyInstance, FilteredElementCollector

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument

targets_input = IN[0] if len(IN) > 0 else []


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


def normalize_text(value):
    if value is None:
        return ""
    return str(value).strip()


def family_type_keys(instance):
    keys = []

    symbol = getattr(instance, "Symbol", None)
    family_name = ""
    type_name = ""

    if symbol is not None:
        type_name = normalize_text(getattr(symbol, "Name", ""))
        family = getattr(symbol, "Family", None)
        if family is not None:
            family_name = normalize_text(getattr(family, "Name", ""))

    if family_name and type_name:
        keys.append("{0}:{1}".format(family_name, type_name))
    if type_name:
        keys.append(type_name)
    if family_name:
        keys.append(family_name)

    seen = set()
    ordered = []
    for key in keys:
        normalized = normalize_text(key)
        if normalized and normalized not in seen:
            seen.add(normalized)
            ordered.append(normalized)
    return ordered


instances = list(
    FilteredElementCollector(doc)
    .OfClass(FamilyInstance)
    .WhereElementIsNotElementType()
)

index = {}
for instance in instances:
    for key in family_type_keys(instance):
        index.setdefault(key, []).append(instance)


result = []
for group in ensure_list(targets_input):
    group_elements = []
    seen_ids = set()

    for target in ensure_list(group):
        key = normalize_text(target)
        if not key:
            continue

        for instance in index.get(key, []):
            element_id = instance.Id.IntegerValue
            if element_id in seen_ids:
                continue
            seen_ids.add(element_id)
            group_elements.append(instance)

    result.append(group_elements)


OUT = result
