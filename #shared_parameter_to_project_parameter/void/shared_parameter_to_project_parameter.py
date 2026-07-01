import clr
import System

# Revit API 참조 추가
# 카테고리, 파라미터 그룹, 카테고리 유효성 검사에 사용합니다.
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import BuiltInCategory, BuiltInParameterGroup, CategoryType

# Dynamo와 Revit 사이를 연결하는 참조 추가
# 현재 문서 접근과 트랜잭션 처리에 사용합니다.
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

# 현재 열려 있는 Revit 문서 / 앱 객체
doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
app = uiapp.Application

# Dynamo 입력값 설명
# IN[0] : shared parameter txt 파일 경로
# IN[1] : Revit 프로젝트 파라미터 그룹 이름, 예: "PG_DATA"
# IN[2] : True면 Instance 바인딩, False면 Type 바인딩
# IN[3] : Dynamo에서 선택한 Revit 카테고리 목록
# IN[4] : 가져오고 싶은 shared parameter 정의 그룹 이름
#         비워두면 txt 파일 안의 모든 그룹을 가져옵니다.
shared_parameter_path = IN[0]
parameter_group_name = IN[1] if len(IN) > 1 and IN[1] else "PG_DATA"
is_instance_binding = True if len(IN) < 3 or IN[2] is None else bool(IN[2])
selected_category_input = IN[3] if len(IN) > 3 else None
selected_definition_group_input = IN[4] if len(IN) > 4 else None


def normalize_text(value):
    # None 값을 안전하게 처리하고 앞뒤 공백을 제거합니다.
    if value is None:
        return None
    return str(value).strip()


def parse_parameter_group(group_name):
    # 사용자가 입력한 문자열(예: "PG_DATA")을
    # Revit의 BuiltInParameterGroup 열거형으로 바꿉니다.
    # 잘못된 값이면 기본값으로 PG_DATA를 사용합니다.
    safe_name = normalize_text(group_name) or "PG_DATA"
    try:
        return System.Enum.Parse(BuiltInParameterGroup, safe_name, True)
    except Exception:
        return BuiltInParameterGroup.PG_DATA


def unwrap_revit_category(value):
    # Dynamo 카테고리 노드는 실제 Revit Category를 감싼(wrapper)
    # 객체를 반환할 수 있습니다.
    # 가능하면 그 안의 실제 Revit Category를 꺼냅니다.
    if value is None:
        return None

    if hasattr(value, "InternalCategory") and value.InternalCategory is not None:
        return value.InternalCategory

    if hasattr(value, "Id") and hasattr(value, "Name") and hasattr(value, "AllowsBoundParameters"):
        return value

    return None


def get_category_by_builtin_name(document, category_name):
    # "OST_Walls" 같은 BuiltInCategory 이름도 지원하고,
    # "Walls" 같은 일반 카테고리 이름도 지원합니다.
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

    # 위 방법으로 못 찾으면 Revit에 보이는 카테고리 이름으로 한 번 더 찾습니다.
    for category in document.Settings.Categories:
        try:
            if category.Name == safe_name:
                return category
        except Exception:
            continue

    return None


def resolve_selected_category(document, value):
    # Dynamo 입력값 1개를 Revit Category 1개로 변환합니다.
    category = unwrap_revit_category(value)
    if category is None:
        category = get_category_by_builtin_name(document, value)
    if category is None:
        raise Exception("Selected category could not be resolved.")

    # 프로젝트 파라미터 바인딩은 모든 카테고리에 가능한 것이 아니므로
    # 모델 카테고리 / 바인딩 가능 / 태그 아님 조건을 검사합니다.
    if category.CategoryType != CategoryType.Model:
        raise Exception("Selected category is not a model category: {0}".format(category.Name))
    if not category.AllowsBoundParameters:
        raise Exception("Selected category does not allow project parameter binding: {0}".format(category.Name))
    if category.IsTagCategory:
        raise Exception("Selected category is a tag category and cannot be bound: {0}".format(category.Name))

    return category


def to_sequence(value):
    # Dynamo 입력은 단일 값일 수도 있고 리스트일 수도 있습니다.
    # 이후 로직을 단순하게 만들기 위해 항상 Python 리스트로 맞춥니다.
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


def parse_name_list(value):
    # "GroupA, GroupB" 같은 문자열을
    # ["GroupA", "GroupB"] 형태의 리스트로 바꿉니다.
    # 중복된 이름은 제거합니다.
    #
    # 추가로, 앞단 선택 Python 노드가 dict 형태로 넘겨주는
    # {"selectedGroups": [...]} 또는 {"csv": "..."}도 처리합니다.
    names = []
    seen = set()

    if isinstance(value, dict):
        if isinstance(value.get("selectedGroups"), list):
            value = value.get("selectedGroups")
        elif value.get("csv") is not None:
            value = value.get("csv")

    for raw_value in to_sequence(value):
        if raw_value is None:
            continue

        for part in str(raw_value).split(","):
            name = normalize_text(part)
            if not name:
                continue

            lowered = name.lower()
            if lowered in seen:
                continue

            seen.add(lowered)
            names.append(name)

    return names


def resolve_selected_categories(document, values):
    # 사용자가 선택한 카테고리들을 모두 Revit Category로 바꾸고
    # 같은 카테고리가 여러 번 들어오면 중복 제거합니다.
    resolved = []
    seen_ids = set()

    for value in to_sequence(values):
        if value is None:
            continue
        if isinstance(value, bool):
            continue

        normalized_value = normalize_text(value)
        if normalized_value in ["True", "False"]:
            continue

        category = resolve_selected_category(document, value)
        category_id = category.Id.IntegerValue

        if category_id in seen_ids:
            continue

        seen_ids.add(category_id)
        resolved.append(category)

    if not resolved:
        raise Exception("Please select at least one Revit category in Dynamo.")

    return resolved


def build_category_set(categories):
    # Revit 파라미터 바인딩 API는 Python 리스트가 아니라
    # Revit 전용 CategorySet 객체를 요구합니다.
    category_set = app.Create.NewCategorySet()
    for category in categories:
        category_set.Insert(category)
    return category_set


def load_definitions(definition_file, selected_group_names):
    # shared parameter txt 파일 안의 정의(definition)를 읽습니다.
    # 그룹 이름이 지정되었다면 해당 그룹의 정의만 남깁니다.
    definitions = []
    available_groups = []
    selected_lookup = set([name.lower() for name in selected_group_names])

    for group in definition_file.Groups:
        group_name = normalize_text(group.Name) or ""
        available_groups.append(group_name)

        if selected_lookup and group_name.lower() not in selected_lookup:
            continue

        for definition in group.Definitions:
            definitions.append((group_name, definition))

    return definitions, available_groups


def bind_definition(definition, category_set, parameter_group, instance_binding):
    # InstanceBinding 또는 TypeBinding을 만든 뒤
    # Insert로 새 바인딩을 시도하고,
    # 이미 있으면 ReInsert로 갱신을 시도합니다.
    binding = app.Create.NewInstanceBinding(category_set) if instance_binding else app.Create.NewTypeBinding(category_set)

    try:
        inserted = doc.ParameterBindings.Insert(definition, binding, parameter_group)
    except TypeError:
        inserted = doc.ParameterBindings.Insert(definition, binding)

    if inserted:
        return "inserted"

    try:
        updated = doc.ParameterBindings.ReInsert(definition, binding, parameter_group)
    except TypeError:
        updated = doc.ParameterBindings.ReInsert(definition, binding)

    if updated:
        return "updated"

    return "failed"


# 1단계. Dynamo 입력값을 실제 작업에 쓰기 좋은 형태로 정리합니다.
file_path = normalize_text(shared_parameter_path)
parameter_group = parse_parameter_group(parameter_group_name)
selected_definition_groups = parse_name_list(selected_definition_group_input)
original_shared_parameter_path = app.SharedParametersFilename

# 2단계. Revit 문서를 건드리기 전에 필수 입력값을 먼저 검사합니다.
if not file_path:
    raise Exception("Shared parameter txt path is empty.")
if selected_category_input is None:
    raise Exception("Please select at least one Revit category in Dynamo.")

# 3단계. Revit이 읽을 shared parameter txt 파일 경로를 지정합니다.
app.SharedParametersFilename = file_path
definition_file = app.OpenSharedParameterFile()

if definition_file is None:
    app.SharedParametersFilename = original_shared_parameter_path
    raise Exception("Shared parameter txt file could not be opened: {0}".format(file_path))

# 4단계. 선택한 카테고리를 해석하고,
# 가져오고 싶은 shared parameter 정의만 수집합니다.
selected_categories = resolve_selected_categories(doc, selected_category_input)
category_set = build_category_set(selected_categories)
definitions, available_groups = load_definitions(definition_file, selected_definition_groups)

# 사용자가 입력한 그룹명이 txt 파일 안에 실제로 있는지 확인합니다.
if selected_definition_groups:
    available_lookup = set([name.lower() for name in available_groups])
    missing_groups = [name for name in selected_definition_groups if name.lower() not in available_lookup]
    if missing_groups:
        app.SharedParametersFilename = original_shared_parameter_path
        raise Exception("Shared parameter group not found: {0}".format(", ".join(missing_groups)))

# 최종적으로 가져올 정의가 하나도 없으면 명확한 메시지로 중단합니다.
if not definitions:
    app.SharedParametersFilename = original_shared_parameter_path
    if selected_definition_groups:
        raise Exception(
            "No shared parameter definitions found in selected group(s): {0}".format(
                ", ".join(selected_definition_groups)
            )
        )
    raise Exception("No shared parameter definitions found in file.")

inserted_names = []
updated_names = []
failed_names = []

# 5단계. Revit 모델을 수정하므로 트랜잭션 안에서 작업해야 합니다.
TransactionManager.Instance.EnsureInTransaction(doc)

try:
    # 6단계. 선택된 정의들을 선택된 카테고리에 프로젝트 파라미터로 바인딩합니다.
    for group_name, definition in definitions:
        status = bind_definition(definition, category_set, parameter_group, is_instance_binding)
        display_name = "{0}/{1}".format(group_name, definition.Name)

        if status == "inserted":
            inserted_names.append(display_name)
        elif status == "updated":
            updated_names.append(display_name)
        else:
            failed_names.append(display_name)
finally:
    # 오류가 나더라도 트랜잭션 종료와
    # 원래 shared parameter 경로 복원은 반드시 수행합니다.
    TransactionManager.Instance.TransactionTaskDone()
    app.SharedParametersFilename = original_shared_parameter_path

# 7단계. Dynamo OUT으로 결과 요약을 돌려줍니다.
OUT = {
    "sharedParameterFile": file_path,
    "categories": [category.Name for category in selected_categories],
    "selectedSharedParameterGroups": selected_definition_groups,
    "availableSharedParameterGroups": available_groups,
    "bindingType": "Instance" if is_instance_binding else "Type",
    "parameterGroup": str(parameter_group),
    "categoryCount": len(selected_categories),
    "definitionCount": len(definitions),
    "inserted": inserted_names,
    "updated": updated_names,
    "failed": failed_names,
}
