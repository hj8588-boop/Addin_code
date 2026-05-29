import clr
import System
import zipfile
from xml.sax.saxutils import escape

# Revit 요소와 파라미터 값을 읽어오기 위한 참조 추가
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import BuiltInCategory, BuiltInParameter, ElementId, FilteredElementCollector, StorageType

# 현재 Revit 문서를 가져오기 위한 참조 추가
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager

# 현재 열려 있는 Revit 문서
doc = DocumentManager.Instance.CurrentDBDocument

# Dynamo Python 노드 입력값 설명
# IN[0] : Excel 파일 경로 또는 저장할 폴더 경로
# IN[1] : Excel 시트 이름
# IN[2] : 추출할 shared parameter 이름 목록 (쉼표 구분)
# IN[3] : Dynamo에서 선택한 카테고리 목록
excel_path = IN[0]
sheet_name = IN[1] if len(IN) > 1 else "SharedParameters"
parameter_names_input = IN[2] if len(IN) > 2 else ""
selected_categories_input = IN[3] if len(IN) > 3 else None


def normalize_text(value):
    # None을 빈 문자열로 바꿔서 이후 문자열 처리 코드를 단순하게 만듭니다.
    if value is None:
        return ""
    return str(value).strip()


def get_safe_filename_part(value):
    # 파일명에 사용할 수 없는 문자를 "_"로 바꿉니다.
    text = normalize_text(value)
    if not text:
        return "SharedParameters"

    # Dynamo CPython can compare Python str and .NET Char unreliably,
    # so normalize invalid file-name characters to plain Python strings.
    invalid_chars = set('<>:"/\\|?*')
    safe_characters = []
    for char in text:
        safe_characters.append("_" if char in invalid_chars else char)

    safe_text = "".join(safe_characters).strip(" .")
    return safe_text or "SharedParameters"


def to_sequence(value):
    # Dynamo 입력이 단일 값이든 리스트든 항상 리스트처럼 다루기 위한 보조 함수입니다.
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
    # "A, B, C" 같은 입력을 ["A", "B", "C"]로 바꿉니다.
    raw = normalize_text(value)
    if not raw:
        return []
    return [part.strip() for part in raw.split(",") if part and part.strip()]


def get_integer_id(value):
    # Revit ElementId / Python int / 기타 숫자형 입력을 모두
    # 비교하기 쉬운 정수 형태로 바꿉니다.
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


def get_element_id(value):
    # Revit API 메서드는 int가 아니라 ElementId를 요구하는 경우가 많습니다.
    # 따라서 가능하면 Revit ElementId 형태로 다시 맞춰줍니다.
    if value is None:
        return None
    if hasattr(value, "IntegerValue"):
        return value

    integer_id = get_integer_id(value)
    if integer_id is None:
        return None

    try:
        return System.Enum.ToObject(type(value), integer_id)
    except Exception:
        pass

    try:
        return ElementId(integer_id)
    except Exception:
        return None


def unwrap_category(value):
    # Dynamo 카테고리 래퍼에서 실제 Revit Category를 꺼냅니다.
    if value is None:
        return None
    if hasattr(value, "InternalCategory") and value.InternalCategory is not None:
        return value.InternalCategory
    if hasattr(value, "Id") and hasattr(value, "Name"):
        return value
    return None


def get_category_by_name(name):
    # 문자열로 받은 카테고리 이름을 Revit Category로 찾습니다.
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
    # 카테고리 입력은 Dynamo Category 객체일 수도 있고,
    # 쉼표로 구분된 문자열일 수도 있으므로 둘 다 처리합니다.
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


def parse_parameter_names(value):
    # 사용자가 입력한 파라미터 이름 문자열을 중복 없는 목록으로 바꿉니다.
    raw = normalize_text(value)
    if not raw:
        return []

    names = []
    seen = set()
    for part in raw.split(","):
        name = normalize_text(part)
        if not name:
            continue
        lowered = name.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        names.append(name)
    return names


def get_elements(categories):
    # 선택된 카테고리들에 속한 Revit 요소를 모두 모읍니다.
    elements = []
    seen = set()

    for category in categories:
        category_element_id = get_element_id(category.Id)
        if category_element_id is None:
            continue

        collector = FilteredElementCollector(doc).OfCategoryId(category_element_id).WhereElementIsNotElementType()
        for element in collector:
            element_id = get_integer_id(element.Id)
            if element_id is None:
                continue
            if element_id in seen:
                continue
            seen.add(element_id)
            elements.append(element)

    return elements


def get_family_and_type_names(element):
    # 카테고리마다 Family / Type 정보가 노출되는 방식이 달라서
    # 여러 경로를 시도하며 최대한 값을 찾습니다.
    family_name = ""
    type_name = ""

    try:
        type_id = element.GetTypeId()
        if type_id is not None and get_integer_id(type_id) and get_integer_id(type_id) > 0:
            element_type = doc.GetElement(type_id)
            if element_type is not None:
                type_name = normalize_text(getattr(element_type, "Name", ""))
                if not type_name:
                    type_param = element_type.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)
                    if type_param is not None:
                        type_name = normalize_text(type_param.AsString() or type_param.AsValueString())
                if not type_name:
                    type_param = element.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)
                    if type_param is not None:
                        type_name = normalize_text(type_param.AsValueString() or type_param.AsString())

                family_param = element_type.LookupParameter("Family Name")
                if family_param is not None:
                    family_name = normalize_text(family_param.AsString() or family_param.AsValueString())
                if not family_name and hasattr(element_type, "FamilyName"):
                    family_name = normalize_text(element_type.FamilyName)
                if not family_name and hasattr(element_type, "Family") and element_type.Family is not None:
                    family_name = normalize_text(element_type.Family.Name)
    except Exception:
        pass

    if not type_name:
        try:
            type_param = element.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)
            if type_param is not None:
                type_name = normalize_text(type_param.AsValueString() or type_param.AsString())
        except Exception:
            pass

    if not family_name:
        try:
            family_param = element.LookupParameter("Family")
            if family_param is not None:
                family_name = normalize_text(family_param.AsValueString() or family_param.AsString())
        except Exception:
            pass

    return family_name, type_name


def get_parameter_value(parameter):
    # shared parameter 값을 Excel에 쓰기 쉬운 문자열로 바꿉니다.
    if parameter is None:
        return ""

    try:
        display_value = parameter.AsValueString()
        if display_value not in (None, ""):
            return str(display_value)
    except Exception:
        pass

    storage_type = parameter.StorageType

    if storage_type == StorageType.String:
        return parameter.AsString() or ""
    if storage_type == StorageType.Integer:
        return str(parameter.AsInteger())
    if storage_type == StorageType.Double:
        return str(parameter.AsDouble())
    if storage_type == StorageType.ElementId:
        try:
            return str(get_integer_id(parameter.AsElementId()) or "")
        except Exception:
            return ""

    return ""


def discover_shared_parameter_names(elements):
    # 사용자가 파라미터 이름을 지정하지 않으면
    # 선택된 요소들에서 shared parameter 이름을 자동 수집합니다.
    names = set()

    for element in elements:
        for parameter in element.Parameters:
            try:
                if parameter.IsShared:
                    definition = parameter.Definition
                    if definition is not None and definition.Name:
                        names.add(definition.Name)
            except Exception:
                continue

    return sorted(names)


def get_project_parameter_guids():
    # 프로젝트 파라미터로 등록된 shared parameter의 GUID 집합을 반환합니다.
    guids = set()
    try:
        bindings_map = doc.ParameterBindings
        iterator = bindings_map.ForwardIterator()
        while iterator.MoveNext():
            definition = iterator.Key
            try:
                if hasattr(definition, "GUID"):
                    guids.add(str(definition.GUID))
            except Exception:
                pass
    except Exception:
        pass
    return guids


def classify_parameters(names, elements):
    # 각 파라미터 이름을 'project' 또는 'family'로 분류합니다.
    # doc.ParameterBindings에 GUID가 있으면 project parameter, 없으면 family parameter.
    project_guids = get_project_parameter_guids()
    result = {}
    for name in names:
        for element in elements:
            param = element.LookupParameter(name)
            if param is not None and param.IsShared:
                try:
                    guid = str(param.Definition.GUID)
                    result[name] = "project" if guid in project_guids else "family"
                except Exception:
                    result[name] = "family"
                break
        if name not in result:
            result[name] = "family"
    return result


def element_matches_requested_parameters(element, parameter_names):
    # 사용자가 특정 파라미터만 뽑고 싶을 때,
    # 그 파라미터를 가진 요소만 추출하기 위한 필터입니다.
    if not parameter_names:
        return True

    for name in parameter_names:
        parameter = element.LookupParameter(name)
        if parameter is not None and parameter.IsShared:
            return True

    return False


def get_excel_column_name(column_index):
    # 1 -> A, 2 -> B, 27 -> AA 형태로 열 이름을 만듭니다.
    name = ""
    current = column_index
    while current > 0:
        current, remainder = divmod(current - 1, 26)
        name = chr(65 + remainder) + name
    return name


def get_excel_cell_reference(row_index, column_index):
    # 행/열 번호를 Excel 셀 주소(A1, B3 등)로 바꿉니다.
    return "{0}{1}".format(get_excel_column_name(column_index), row_index)


def get_sheet_name(value):
    # Excel 시트 이름은 길이와 특수문자 제한이 있어서 안전하게 정리합니다.
    safe_name = get_safe_filename_part(value)
    safe_name = safe_name.replace("[", "_").replace("]", "_")
    return safe_name[:31] or "Sheet1"


def get_cell_xml(cell_reference, value, style_index=None):
    # xlsx 내부 XML에서 셀 하나를 표현하는 문자열을 만듭니다.
    style_attribute = ""
    if style_index is not None:
        style_attribute = ' s="{0}"'.format(style_index)

    if value is None:
        return '<c r="{0}"{1}/>'.format(cell_reference, style_attribute)

    text = normalize_text(value)
    if text == "":
        return '<c r="{0}"{1} t="inlineStr"><is><t></t></is></c>'.format(
            cell_reference,
            style_attribute
        )

    return '<c r="{0}"{1} t="inlineStr"><is><t xml:space="preserve">{2}</t></is></c>'.format(
        cell_reference,
        style_attribute,
        escape(text)
    )


def build_sheet_xml(matrix, column_types=None):
    # 표 데이터(matrix)를 sheet1.xml 형식의 문자열로 변환합니다.
    # column_types: 컬럼마다 'fixed' / 'project' / 'family' 를 지정합니다.
    # 헤더 행(row 1): fixed=회색+굵게(1), project=파란색+굵게(2), family=초록색+굵게(3)
    # 데이터 행: fixed=스타일없음, project=연파란색(4), family=연초록색(5)
    HEADER_STYLE = {"fixed": 1, "project": 2, "family": 3}
    DATA_STYLE = {"fixed": None, "project": 4, "family": 5}

    rows_xml = []

    for row_index, row_values in enumerate(matrix, 1):
        is_header = row_index == 1
        cell_xml = []
        for column_index, value in enumerate(row_values, 1):
            cell_reference = get_excel_cell_reference(row_index, column_index)
            col_type = "fixed"
            if column_types and column_index <= len(column_types):
                col_type = column_types[column_index - 1]
            if is_header:
                style_index = HEADER_STYLE.get(col_type, 1)
            else:
                style_index = DATA_STYLE.get(col_type)
            cell_xml.append(get_cell_xml(cell_reference, value, style_index))
        rows_xml.append('<row r="{0}" ht="22" customHeight="1">{1}</row>'.format(row_index, "".join(cell_xml)))

    dimension_ref = "A1"
    if matrix and matrix[0]:
        last_cell = get_excel_cell_reference(len(matrix), len(matrix[0]))
        dimension_ref = "A1:{0}".format(last_cell)

    return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="{0}"/>
  <sheetViews>
    <sheetView workbookViewId="0"/>
  </sheetViews>
  <sheetFormatPr defaultRowHeight="22" customHeight="1"/>
  <sheetData>{1}</sheetData>
</worksheet>""".format(dimension_ref, "".join(rows_xml))


def build_workbook_xml(sheet_name):
    # workbook.xml은 "이 xlsx 파일 안에 어떤 시트가 있는지"를 설명합니다.
    return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="{0}" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>""".format(escape(sheet_name))


def write_matrix_to_excel(file_path, worksheet_name, matrix, column_types=None):
    # Excel COM을 사용하지 않고, xlsx 파일 구조를 직접 zip으로 생성합니다.
    # 그래서 Excel이 설치되지 않은 PC에서도 파일 생성이 가능합니다.
    safe_sheet_name = get_sheet_name(worksheet_name)
    directory_path = System.IO.Path.GetDirectoryName(file_path)
    if directory_path and not System.IO.Directory.Exists(directory_path):
        System.IO.Directory.CreateDirectory(directory_path)

    content_types_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>"""

    root_rels_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>"""

    workbook_rels_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>"""

    created_text = System.DateTime.UtcNow.ToString("s") + "Z"
    core_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
 xmlns:dc="http://purl.org/dc/elements/1.1/"
 xmlns:dcterms="http://purl.org/dc/terms/"
 xmlns:dcmitype="http://purl.org/dc/dcmitype/"
 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:creator>OpenAI Codex</dc:creator>
  <cp:lastModifiedBy>OpenAI Codex</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">{0}</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">{0}</dcterms:modified>
</cp:coreProperties>""".format(created_text)

    app_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
 xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>Revit Dynamo Export</Application>
</Properties>"""

    styles_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2">
    <font>
      <sz val="11"/>
      <color theme="1"/>
      <name val="Calibri"/>
      <family val="2"/>
    </font>
    <font>
      <b/>
      <sz val="11"/>
      <color theme="1"/>
      <name val="Calibri"/>
      <family val="2"/>
    </font>
  </fonts>
  <fills count="7">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FFD9D9D9"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FFBDD7EE"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FFA9D18E"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FFDDEBF7"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FFE2EFDA"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
  </fills>
  <borders count="1">
    <border>
      <left/>
      <right/>
      <top/>
      <bottom/>
      <diagonal/>
    </border>
  </borders>
  <cellStyleXfs count="1">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
  </cellStyleXfs>
  <cellXfs count="6">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
    <xf numFmtId="0" fontId="1" fillId="3" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
    <xf numFmtId="0" fontId="1" fillId="4" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
    <xf numFmtId="0" fontId="0" fillId="5" borderId="0" xfId="0" applyFill="1"/>
    <xf numFmtId="0" fontId="0" fillId="6" borderId="0" xfId="0" applyFill="1"/>
  </cellXfs>
  <cellStyles count="1">
    <cellStyle name="Normal" xfId="0" builtinId="0"/>
  </cellStyles>
</styleSheet>"""

    with zipfile.ZipFile(file_path, "w", zipfile.ZIP_DEFLATED) as workbook_zip:
        workbook_zip.writestr("[Content_Types].xml", content_types_xml)
        workbook_zip.writestr("_rels/.rels", root_rels_xml)
        workbook_zip.writestr("docProps/core.xml", core_xml)
        workbook_zip.writestr("docProps/app.xml", app_xml)
        workbook_zip.writestr("xl/workbook.xml", build_workbook_xml(safe_sheet_name))
        workbook_zip.writestr("xl/_rels/workbook.xml.rels", workbook_rels_xml)
        workbook_zip.writestr("xl/styles.xml", styles_xml)
        workbook_zip.writestr("xl/worksheets/sheet1.xml", build_sheet_xml(matrix, column_types))


def resolve_output_excel_path(path_value, worksheet_name):
    # 사용자가 파일 경로를 넣었는지, 폴더 경로를 넣었는지 판단해서
    # 최종 저장할 xlsx 경로를 만듭니다.
    raw_path = normalize_text(path_value)
    if not raw_path:
        raise Exception("Excel folder path is empty.")

    full_path = System.IO.Path.GetFullPath(raw_path)
    extension = normalize_text(System.IO.Path.GetExtension(full_path)).lower()

    if System.IO.Directory.Exists(full_path):
        directory_path = full_path
    elif extension in [".xlsx", ".xlsm", ".xls"]:
        directory_path = System.IO.Path.GetDirectoryName(full_path)
        if directory_path and not System.IO.Directory.Exists(directory_path):
            System.IO.Directory.CreateDirectory(directory_path)
        return full_path
    else:
        directory_path = full_path
        if not System.IO.Directory.Exists(directory_path):
            System.IO.Directory.CreateDirectory(directory_path)

    timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
    safe_sheet_name = get_safe_filename_part(worksheet_name)
    file_name = "SharedParameterValues_{0}_{1}.xlsx".format(safe_sheet_name, timestamp)
    return System.IO.Path.Combine(directory_path, file_name)


# 입력값 정리
worksheet_name = normalize_text(sheet_name) or "SharedParameters"
file_path = resolve_output_excel_path(excel_path, worksheet_name)
selected_categories = resolve_categories(selected_categories_input)

# 필수 입력 검증
if not selected_categories:
    raise Exception("Please select at least one Revit category in Dynamo.")

# 요소 수집 -> 파라미터 필터링 -> 헤더 생성 순서로 export 데이터를 구성합니다.
requested_parameter_names = parse_parameter_names(parameter_names_input)
elements = get_elements(selected_categories)

filtered_elements = []
for element in elements:
    if element_matches_requested_parameters(element, requested_parameter_names):
        filtered_elements.append(element)

parameter_names = requested_parameter_names or discover_shared_parameter_names(filtered_elements)

# 파라미터를 project / family 로 분류하고 컬럼 타입 목록을 만듭니다.
param_classification = classify_parameters(parameter_names, filtered_elements)
column_types = ["fixed", "fixed", "fixed", "fixed"]
column_types.extend(param_classification.get(name, "family") for name in parameter_names)

# 앞부분 고정 컬럼 + shared parameter 컬럼으로 Excel 헤더를 만듭니다.
headers = ["ElementId", "Category", "Family", "Type"]
headers.extend(parameter_names)

rows = [headers]

# 각 Revit 요소를 한 줄(row)로 변환합니다.
for element in filtered_elements:
    category_name = ""
    try:
        category_name = normalize_text(element.Category.Name)
    except Exception:
        pass

    family_name, type_name = get_family_and_type_names(element)

    row = [
        get_integer_id(element.Id),
        category_name,
        family_name,
        type_name
    ]

    for parameter_name in parameter_names:
        parameter = element.LookupParameter(parameter_name)
        if parameter is None or not parameter.IsShared:
            row.append("")
        else:
            row.append(get_parameter_value(parameter))

    rows.append(row)

write_matrix_to_excel(file_path, worksheet_name, rows, column_types)

# Dynamo OUT 결과 요약
OUT = {
    "excelPath": file_path,
    "sheetName": worksheet_name,
    "categories": [normalize_text(category.Name) for category in selected_categories],
    "categoryCount": len(selected_categories),
    "elementCount": len(filtered_elements),
    "parameterCount": len(parameter_names),
    "parameters": parameter_names
}
