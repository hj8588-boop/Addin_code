import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    ElementTransformUtils,
    FamilyInstance,
    GeometryInstance,
    ImportInstance,
    LocationPoint,
    Options,
    PlanarFace,
    Solid,
    SolidUtils,
    Transform,
    UnitUtils,
    XYZ,
)

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
uidoc = uiapp.ActiveUIDocument if uiapp else None

# IN[0]: 최대 탐색 거리(mm)
max_distance_mm = IN[0] if len(IN) > 0 else 3000


def to_float(value, default_value):
    """숫자로 바꿀 수 없으면 기본값을 사용합니다."""
    try:
        return float(value)
    except Exception:
        return default_value


def to_internal_mm(value_in_mm):
    """mm 값을 Revit 내부 단위로 바꿉니다."""
    try:
        from Autodesk.Revit.DB import UnitTypeId
        return UnitUtils.ConvertToInternalUnits(value_in_mm, UnitTypeId.Millimeters)
    except Exception:
        return float(value_in_mm) / 304.8


def from_internal_mm(value_in_internal_units):
    """Revit 내부 단위를 mm로 바꿉니다."""
    try:
        from Autodesk.Revit.DB import UnitTypeId
        return UnitUtils.ConvertFromInternalUnits(value_in_internal_units, UnitTypeId.Millimeters)
    except Exception:
        return float(value_in_internal_units) * 304.8


def safe_int_id(element):
    """요소 ID를 안전하게 읽습니다."""
    try:
        return element.Id.IntegerValue
    except Exception:
        return None


def xyz_to_tuple(point):
    """XYZ 좌표를 Watch에서 읽기 쉬운 튜플로 바꿉니다."""
    if point is None:
        return None

    try:
        return (round(point.X, 6), round(point.Y, 6), round(point.Z, 6))
    except Exception:
        return None


def normalize_xyz(vector):
    """벡터를 단위 벡터로 바꿉니다."""
    if vector is None:
        return None

    try:
        if vector.GetLength() < 1e-9:
            return None
        return vector.Normalize()
    except Exception:
        return None


def get_symbol_label(symbol):
    """Family / Type 이름을 한 줄 문자열로 만듭니다."""
    try:
        family_name = symbol.Family.Name
    except Exception:
        family_name = "UnknownFamily"

    try:
        type_name = symbol.Name
    except Exception:
        type_name = "UnknownType"

    return "{0} : {1}".format(family_name, type_name)


def get_selected_elements():
    """현재 Revit 선택 요소 전체를 읽습니다."""
    if uidoc is None:
        return []

    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def split_selection(elements):
    """
    선택 요소를 패밀리와 Import Symbol로 분리합니다.

    사용 방법:
    - 이동할 FamilyInstance 하나 이상 선택
    - 기준이 될 Import Symbol 하나 선택
    """
    families = []
    imports = []

    for element in elements:
        if isinstance(element, FamilyInstance):
            families.append(element)
        elif isinstance(element, ImportInstance):
            imports.append(element)

    return families, imports


def get_instance_point(instance):
    """
    패밀리 기준점(LocationPoint)을 읽습니다.

    초보자 설명:
    - 이 점을 기준으로 가장 가까운 top face를 찾습니다.
    - 이후 패밀리를 그 면의 목표점으로 이동시킵니다.
    """
    location = instance.Location
    if isinstance(location, LocationPoint):
        return location.Point
    return None


def get_import_geometry_options():
    """Import Symbol geometry를 읽기 위한 옵션입니다."""
    options = Options()
    options.IncludeNonVisibleObjects = True
    options.ComputeReferences = False
    return options


def transform_solid_if_needed(solid, transform):
    """필요한 경우 Solid에 transform을 적용합니다."""
    if solid is None:
        return None

    try:
        if solid.Volume <= 1e-9:
            return None
    except Exception:
        pass

    try:
        return SolidUtils.CreateTransformed(solid, transform)
    except Exception:
        return solid


def collect_top_faces(geometry_element, current_transform, output_faces):
    """
    Import Symbol 내부에서 위쪽을 향한 PlanarFace만 수집합니다.

    초보자 설명:
    - 이번 버전은 선(line)이 아니라 '상부면(top face)' 기준 배치입니다.
    - 그래서 Solid를 읽어서 위쪽을 향한 평면 면만 후보로 남깁니다.
    """
    if geometry_element is None:
        return

    for geometry_object in geometry_element:
        if isinstance(geometry_object, GeometryInstance):
            try:
                nested_geometry = geometry_object.GetSymbolGeometry()
            except Exception:
                nested_geometry = None

            next_transform = current_transform
            try:
                instance_transform = geometry_object.Transform
                if next_transform is None:
                    next_transform = instance_transform
                else:
                    next_transform = next_transform.Multiply(instance_transform)
            except Exception:
                pass

            collect_top_faces(nested_geometry, next_transform, output_faces)
            continue

        if not isinstance(geometry_object, Solid):
            continue

        solid = transform_solid_if_needed(geometry_object, current_transform)
        if solid is None:
            continue

        try:
            faces = solid.Faces
        except Exception:
            faces = []

        for face in faces:
            if not isinstance(face, PlanarFace):
                continue

            try:
                normal = normalize_xyz(face.FaceNormal)
            except Exception:
                normal = None

            if normal is None:
                continue

            # 위쪽을 향한 면만 top face로 인정합니다.
            if normal.Z < 0.7:
                continue

            output_faces.append({
                "face": face,
                "normal": normal,
            })


def find_nearest_top_face_point(source_point, top_faces, max_distance_internal):
    """
    선택된 패밀리 점에서 가장 가까운 상부면 점을 찾습니다.
    """
    best_match = None
    best_distance = None

    for item in top_faces:
        face = item["face"]

        try:
            projection = face.Project(source_point)
        except Exception:
            projection = None

        if projection is None:
            continue

        try:
            target_point = projection.XYZPoint
        except Exception:
            target_point = None

        if target_point is None:
            continue

        distance = source_point.DistanceTo(target_point)
        if max_distance_internal > 0 and distance > max_distance_internal:
            continue

        if best_distance is None or distance < best_distance:
            best_distance = distance
            best_match = {
                "targetPoint": target_point,
                "distance": distance,
                "normal": item["normal"],
            }

    return best_match


def make_result(**kwargs):
    """Dynamo Watch에서 보기 쉬운 결과 형식을 유지합니다."""
    result = {
        "selectedFamilyCount": 0,
        "selectedImportCount": 0,
        "topFaceCount": 0,
        "movedIds": [],
        "moveDetails": [],
        "skipped": [],
        "message": None,
    }
    result.update(kwargs)
    return result


def main():
    selected_elements = get_selected_elements()
    selected_families, selected_imports = split_selection(selected_elements)
    max_distance_value = max(0.0, to_float(max_distance_mm, 3000.0))
    max_distance_internal = to_internal_mm(max_distance_value)

    if not selected_families:
        return make_result(
            selectedFamilyCount=0,
            selectedImportCount=len(selected_imports),
            message="Select one or more family instances and exactly one Import Symbol, then run Dynamo.",
        )

    if len(selected_imports) != 1:
        return make_result(
            selectedFamilyCount=len(selected_families),
            selectedImportCount=len(selected_imports),
            message="Select exactly one Import Symbol together with the family instances, then run Dynamo.",
        )

    target_import = selected_imports[0]

    try:
        geometry_element = target_import.get_Geometry(get_import_geometry_options())
    except Exception as exc:
        return make_result(
            selectedFamilyCount=len(selected_families),
            selectedImportCount=1,
            message="Failed to read geometry from the selected Import Symbol.",
            skipped=[{
                "reason": "Geometry read failed.",
                "importId": safe_int_id(target_import),
                "error": "{0}: {1}".format(type(exc).__name__, exc),
            }],
        )

    top_faces = []
    collect_top_faces(geometry_element, Transform.Identity, top_faces)

    if not top_faces:
        return make_result(
            selectedFamilyCount=len(selected_families),
            selectedImportCount=1,
            topFaceCount=0,
            message="No usable top planar faces were found in the selected Import Symbol.",
        )

    moved_ids = []
    move_details = []
    skipped = []

    TransactionManager.Instance.EnsureInTransaction(doc)

    try:
        for family in selected_families:
            family_point = get_instance_point(family)
            if family_point is None:
                skipped.append({
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family.Symbol),
                    "reason": "Family has no usable LocationPoint.",
                })
                continue

            nearest_match = find_nearest_top_face_point(family_point, top_faces, max_distance_internal)
            if nearest_match is None:
                skipped.append({
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family.Symbol),
                    "reason": "No nearby top face target was found inside the search distance.",
                })
                continue

            move_vector = nearest_match["targetPoint"].Subtract(family_point)

            try:
                ElementTransformUtils.MoveElement(doc, family.Id, move_vector)
                moved_ids.append(family.Id.IntegerValue)
                move_details.append({
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family.Symbol),
                    "importId": safe_int_id(target_import),
                    "distanceMm": round(from_internal_mm(nearest_match["distance"]), 2),
                    "targetPoint": xyz_to_tuple(nearest_match["targetPoint"]),
                })
            except Exception as exc:
                skipped.append({
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family.Symbol),
                    "importId": safe_int_id(target_import),
                    "reason": "MoveElement failed.",
                    "error": "{0}: {1}".format(type(exc).__name__, exc),
                })
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    return make_result(
        selectedFamilyCount=len(selected_families),
        selectedImportCount=1,
        topFaceCount=len(top_faces),
        movedIds=moved_ids,
        moveDetails=move_details,
        skipped=skipped,
        message="Completed. The selected Import Symbol was treated as a solid top-face host and families were moved to the nearest top face point.",
    )


OUT = main()
