import clr
import System
import zipfile
import xml.etree.ElementTree as ET

# Revit 요소와 파라미터를 수정하기 위한 참조 추가
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import BuiltInCategory, ElementId, StorageType

# 현재 문서 접근과 트랜잭션 처리를 위한 참조 추가
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

# 현재 열려 있는 Revit 문서
doc = DocumentManager.Instance.CurrentDBDocument

# Dynamo Python 노드 입력값 설명
# IN[0] : 읽어올 xlsx 파일 경로
# IN[1] : 읽어올 시트 이름
# IN[2] : 값을 적용할 카테고리 목록
excel_path = IN[0]
sheet_name = IN[1] if len(IN) > 1 else "SharedParameters"
selected_categories_input = IN[2] if len(IN) > 2 else None


def normalize_text(value):
    # None 값을 빈 문자열로 바꿔서 비교를 쉽게 합니다.
    if value is None:
        return ""
    return str(value).strip()


def to_sequence(value):
    # Dynamo 입력이 단일 값이든 리스트든 항상 리스트로 맞춥니다.
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


def split_csv_text(value):
    # 쉼표로 구분된 문자열을 리스트로 나눕니다.
    raw = normalize_text(value)
    if not raw:
        return []
    return [part.strip() for part in raw.split(",") if part and part.strip()]


def get_integer_id(value):
    # Revit ElementId와 Python int를 같은 방식으로 비교하기 위해
    # 정수값으로 통일합니다.
    if value is None:
        return None
    if isinstance(value, int):
        return value
    if hasattr(value, "IntegerValue"):
        return value.IntegerValue
    try:
        return int(value)
    except Exception:
        return None


def unwrap_category(value):
    # Dynamo Category 래퍼에서 실제 Revit Category를 꺼냅니다.
    if value is None:
        return None
    if hasattr(value, "InternalCategory") and value.InternalCategory is not None:
        return value.InternalCategory
    if hasattr(value, "Id") and hasattr(value, "Name"):
        return value
    return None


def get_category_by_name(name):
    # 문자열 카테고리 이름을 Revit Category 객체로 변환합니다.
    safe_name = normalize_text(name)
    if not safe_name:
        return None

    builtin_name = safe_name if safe_name.startswith("OST_") else "OST_{0}".format(safe_name)

    try:
        builtin_category = System.Enum.Parse(BuiltInCategory, builtin_name, True)
        category = doc.Settings.Categories.get_Item(builtin_category)
        if category is not None:
            return category
    except Exception:
        pass

    for category in doc.Settings.Categories:
        try:
            if category.Name == safe_name:
                return category
        except Exception:
            continue

    return None


def resolve_categories(values):
    # 카테고리 객체 입력과 문자열 입력을 모두 처리합니다.
    categories = []
    seen = set()

    raw_values = []
    for value in to_sequence(values):
        category = unwrap_category(value)
        if category is not None:
            raw_values.append(category)
            continue

        split_values = split_csv_text(value)
        if split_values:
            raw_values.extend(split_values)
        else:
            raw_values.append(value)

    for value in raw_values:
        category = unwrap_category(value)
        if category is None:
            category = get_category_by_name(value)
        if category is None:
            continue

        category_id = get_integer_id(category.Id)
        if category_id is None:
            continue
        if category_id in seen:
            continue

        seen.add(category_id)
        categories.append(category)

    return categories


EXCEL_MAIN_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
EXCEL_REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PACKAGE_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"


def get_xml_root(zip_file, internal_path):
    # xlsx는 zip 파일이므로 내부 XML 파일 하나를 읽어 파싱합니다.
    return ET.fromstring(zip_file.read(internal_path))


def get_column_index_from_reference(cell_reference):
    # "C12" 같은 셀 주소에서 열 문자만 읽어 숫자 열 번호로 바꿉니다.
    letters = []
    for char in cell_reference:
        if char.isalpha():
            letters.append(char.upper())
        else:
            break

    column_index = 0
    for char in letters:
        column_index = (column_index * 26) + (ord(char) - 64)

    return column_index


def get_shared_strings(zip_file):
    # xlsx는 문자열을 sharedStrings.xml에 따로 저장할 수 있습니다.
    # 이 목록을 먼저 읽어두면 셀 값 해석이 쉬워집니다.
    shared_strings_path = "xl/sharedStrings.xml"
    if shared_strings_path not in zip_file.namelist():
        return []

    root = get_xml_root(zip_file, shared_strings_path)
    values = []

    for string_item in root.findall("{{{0}}}si".format(EXCEL_MAIN_NS)):
        text_parts = []
        for text_node in string_item.iterfind(".//{{{0}}}t".format(EXCEL_MAIN_NS)):
            text_parts.append(text_node.text or "")
        values.append("".join(text_parts))

    return values


def get_sheet_path(zip_file, worksheet_name):
    # workbook.xml에서 시트 이름을 찾고,
    # 실제 sheet xml 파일 경로를 관계(relationship)로 따라갑니다.
    workbook_root = get_xml_root(zip_file, "xl/workbook.xml")
    relationship_id = None

    for sheet in workbook_root.findall(".//{{{0}}}sheet".format(EXCEL_MAIN_NS)):
        if normalize_text(sheet.get("name")) == worksheet_name:
            relationship_id = sheet.get("{{{0}}}id".format(EXCEL_REL_NS))
            break

    if not relationship_id:
        raise Exception("Worksheet not found: {0}".format(worksheet_name))

    relationships_root = get_xml_root(zip_file, "xl/_rels/workbook.xml.rels")
    for relationship in relationships_root.findall("{{{0}}}Relationship".format(PACKAGE_REL_NS)):
        if relationship.get("Id") == relationship_id:
            target = relationship.get("Target")
            return "xl/{0}".format(target.replace("\\", "/"))

    raise Exception("Worksheet relationship not found: {0}".format(worksheet_name))


def get_cell_text(cell, shared_strings):
    # 셀 타입에 따라 inline 문자열 / shared string / 일반 값을 해석합니다.
    cell_type = cell.get("t")
    value_node = cell.find("{{{0}}}v".format(EXCEL_MAIN_NS))

    if cell_type == "inlineStr":
        text_parts = []
        for text_node in cell.iterfind(".//{{{0}}}t".format(EXCEL_MAIN_NS)):
            text_parts.append(text_node.text or "")
        return "".join(text_parts)

    if value_node is None:
        return ""

    raw_value = value_node.text or ""

    if cell_type == "s":
        try:
            return shared_strings[int(raw_value)]
        except Exception:
            return ""

    return raw_value


def read_sheet_matrix(file_path, worksheet_name):
    # 최종적으로 Excel 시트를 2차원 리스트(matrix) 형태로 읽어옵니다.
    matrix = []

    with zipfile.ZipFile(file_path, "r") as workbook_zip:
        shared_strings = get_shared_strings(workbook_zip)
        sheet_path = get_sheet_path(workbook_zip, worksheet_name)
        sheet_root = get_xml_root(workbook_zip, sheet_path)

        for row_node in sheet_root.findall(".//{{{0}}}sheetData/{{{0}}}row".format(EXCEL_MAIN_NS)):
            row_values = []
            current_column = 1

            for cell in row_node.findall("{{{0}}}c".format(EXCEL_MAIN_NS)):
                cell_reference = cell.get("r") or ""
                column_index = get_column_index_from_reference(cell_reference) if cell_reference else current_column

                while current_column < column_index:
                    row_values.append("")
                    current_column += 1

                row_values.append(get_cell_text(cell, shared_strings))
                current_column += 1

            matrix.append(row_values)

    return matrix


def get_element_from_row(row, header_index):
    # Excel 한 줄(row)에서 어떤 Revit 요소를 수정할지 찾습니다.
    # 가능하면 UniqueId를 먼저 쓰고, 없으면 ElementId를 사용합니다.
    unique_id_index = header_index.get("UniqueId")
    element_id_index = header_index.get("ElementId")

    if unique_id_index is not None and unique_id_index < len(row):
        unique_id = normalize_text(row[unique_id_index])
        if unique_id:
            try:
                element = doc.GetElement(unique_id)
                if element is not None:
                    return element
            except Exception:
                pass

    if element_id_index is not None and element_id_index < len(row):
        element_id_text = normalize_text(row[element_id_index])
        if element_id_text:
            try:
                element_id = int(float(element_id_text))
                return doc.GetElement(ElementId(element_id))
            except Exception:
                pass

    return None


def set_parameter_value(parameter, raw_value):
    # Excel에서 읽은 문자열을 Revit 파라미터 타입에 맞게 변환해서 저장합니다.
    text = normalize_text(raw_value)
    if text == "":
        return "skipped-empty"

    storage_type = parameter.StorageType

    if storage_type == StorageType.String:
        parameter.Set(text)
        return "updated"

    if storage_type == StorageType.Integer:
        lowered = text.lower()
        if lowered in ("true", "yes", "y"):
            parameter.Set(1)
        elif lowered in ("false", "no", "n"):
            parameter.Set(0)
        else:
            parameter.Set(int(float(text)))
        return "updated"

    if storage_type == StorageType.Double:
        try:
            if parameter.SetValueString(text):
                return "updated"
        except Exception:
            pass
        parameter.Set(float(text))
        return "updated"

    if storage_type == StorageType.ElementId:
        return "unsupported-elementid"

    return "unsupported"


# 입력값 정리
file_path = normalize_text(excel_path)
worksheet_name = normalize_text(sheet_name) or "SharedParameters"
selected_categories = resolve_categories(selected_categories_input)

# 필수 입력 검증
if not file_path:
    raise Exception("Excel file path is empty.")
if not System.IO.File.Exists(file_path):
    raise Exception("Excel file does not exist: {0}".format(file_path))
if not selected_categories:
    raise Exception("Please select at least one Revit category in Dynamo.")

# 사용자가 선택한 카테고리에 해당하는 요소만 수정하기 위해
# 허용된 카테고리 ID 목록을 미리 만들어 둡니다.
allowed_category_ids = set([get_integer_id(category.Id) for category in selected_categories if get_integer_id(category.Id) is not None])
matrix = read_sheet_matrix(file_path, worksheet_name)

if len(matrix) < 2:
    raise Exception("The worksheet does not contain any data rows.")

headers = [normalize_text(value) for value in matrix[0]]
header_index = {}
for index, header in enumerate(headers):
    if header and header not in header_index:
        header_index[header] = index

# 이 컬럼들은 식별/참고용 컬럼이라 실제 파라미터 업데이트 대상이 아닙니다.
fixed_headers = set(["ElementId", "UniqueId", "Category", "Family", "Type"])
parameter_columns = []
for index, header in enumerate(headers):
    if not header or header in fixed_headers:
        continue
    parameter_columns.append((index, header))

updated = []
skipped = []
failed = []

# Revit 모델 데이터를 수정하므로 트랜잭션이 필요합니다.
TransactionManager.Instance.EnsureInTransaction(doc)

try:
    # 헤더 다음 줄부터 실제 데이터 행을 순회합니다.
    for row_index, row in enumerate(matrix[1:], 2):
        element = get_element_from_row(row, header_index)
        if element is None:
            skipped.append("Row {0}: element not found".format(row_index))
            continue

        try:
            category_id = get_integer_id(element.Category.Id)
        except Exception:
            skipped.append("Row {0}: element has no category".format(row_index))
            continue

        if category_id not in allowed_category_ids:
            skipped.append("Row {0}: category not in selected list".format(row_index))
            continue

        changed_names = []

        for column_index, parameter_name in parameter_columns:
            if column_index >= len(row):
                continue

            value = row[column_index]
            parameter = element.LookupParameter(parameter_name)

            if parameter is None:
                failed.append("Row {0}: parameter not found [{1}]".format(row_index, parameter_name))
                continue
            if not parameter.IsShared:
                failed.append("Row {0}: parameter is not shared [{1}]".format(row_index, parameter_name))
                continue
            if parameter.IsReadOnly:
                failed.append("Row {0}: parameter is read-only [{1}]".format(row_index, parameter_name))
                continue

            try:
                status = set_parameter_value(parameter, value)
            except Exception as ex:
                failed.append("Row {0}: {1} [{2}]".format(row_index, str(ex), parameter_name))
                continue

            if status == "updated":
                changed_names.append(parameter_name)
            elif status == "unsupported-elementid":
                failed.append("Row {0}: ElementId storage is not supported [{1}]".format(row_index, parameter_name))

        if changed_names:
            updated.append({
                "row": row_index,
                "elementId": get_integer_id(element.Id),
                "parameters": changed_names
            })
finally:
    TransactionManager.Instance.TransactionTaskDone()

# Dynamo OUT 결과 요약
OUT = {
    "excelPath": file_path,
    "sheetName": worksheet_name,
    "updatedCount": len(updated),
    "updated": updated,
    "skipped": skipped,
    "failed": failed
}
