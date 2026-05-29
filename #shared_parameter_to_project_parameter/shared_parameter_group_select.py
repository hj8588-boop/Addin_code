import clr

clr.AddReference("RevitServices")

# Dynamo Python 노드 입력값 설명
# IN[0] : 그룹 목록 조회 Python 노드의 OUT 결과(dict)
# IN[1] : 선택할 그룹 인덱스 문자열 (예: "0" 또는 "0,2,3")
group_list_result = IN[0]
selected_index_input = IN[1] if len(IN) > 1 else ""


def normalize_text(value):
    # None 값을 빈 문자열로 바꿔 안전하게 처리합니다.
    if value is None:
        return ""
    return str(value).strip()


def to_sequence(value):
    # 단일 값 / 리스트 입력을 모두 리스트처럼 다루기 위한 함수입니다.
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        return list(value)
    return [value]


def parse_index_list(value):
    # "0,2,3" 같은 문자열을 [0, 2, 3] 형태로 변환합니다.
    indexes = []
    seen = set()

    for raw_value in to_sequence(value):
        for part in normalize_text(raw_value).split(","):
            text = normalize_text(part)
            if not text:
                continue

            index = int(text)
            if index in seen:
                continue

            seen.add(index)
            indexes.append(index)

    return indexes


def get_group_names(result):
    # 앞 단계 Python 노드 OUT에서 groups 리스트만 안전하게 꺼냅니다.
    if isinstance(result, dict):
        groups = result.get("groups", [])
        if isinstance(groups, list):
            return groups
    return []


groups = get_group_names(group_list_result)
selected_indexes = parse_index_list(selected_index_input)

if not groups:
    raise Exception("No shared parameter groups are available to select.")

if not selected_indexes:
    raise Exception("Please enter at least one group index.")

selected_groups = []
for index in selected_indexes:
    if index < 0 or index >= len(groups):
        raise Exception("Group index out of range: {0}".format(index))
    selected_groups.append(groups[index])


# Dynamo OUT 결과 요약
OUT = {
    "selectedIndexes": selected_indexes,
    "selectedGroups": selected_groups,
    "csv": ", ".join(selected_groups)
}
