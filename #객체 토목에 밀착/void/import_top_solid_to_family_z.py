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
    SubTransaction,
    UnitUtils,
    XYZ,
)

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
uidoc = uiapp.ActiveUIDocument if uiapp else None

# IN[0]: Max Search Distance Mm
max_distance_mm = IN[0] if len(IN) > 0 else 5000
local_xy_tolerance_mm = 300


def to_float(value, default_value):
    """숫자로 바꿀 수 있으면 바꾸고, 실패하면 기본값을 씁니다."""
    try:
        return float(value)
    except Exception:
        return default_value


def to_internal_mm(value_in_mm):
    """밀리미터(mm)를 Revit 내부 단위(feet)로 바꿉니다."""
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertToInternalUnits(value_in_mm, UnitTypeId.Millimeters)
    except Exception:
        return float(value_in_mm) / 304.8


def from_internal_mm(value_in_internal_units):
    """Revit 내부 단위를 밀리미터(mm)로 바꿉니다."""
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertFromInternalUnits(
            value_in_internal_units,
            UnitTypeId.Millimeters,
        )
    except Exception:
        return float(value_in_internal_units) * 304.8


def safe_int_id(element):
    """요소 ID를 정수로 돌려줍니다."""
    try:
        return element.Id.IntegerValue
    except Exception:
        return None


def xyz_to_list(point):
    """XYZ 좌표를 Watch에서 보기 쉬운 리스트로 바꿉니다."""
    if point is None:
        return None

    try:
        return [round(point.X, 6), round(point.Y, 6), round(point.Z, 6)]
    except Exception:
        return None


def get_symbol_label(instance):
    """패밀리 이름과 타입 이름을 한 줄로 보여줍니다."""
    try:
        symbol = instance.Symbol
        return "{0} : {1}".format(symbol.Family.Name, symbol.Name)
    except Exception:
        return "UnknownFamily : UnknownType"


def get_selected_elements():
    """현재 Revit에서 선택된 요소를 모두 가져옵니다."""
    if uidoc is None:
        return []

    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def split_selection(elements):
    """
    선택된 요소를 장비 패밀리와 Import Symbol로 나눕니다.

    사용 방법:
    - 이동할 FamilyInstance를 하나 이상 선택
    - 기준이 될 ImportInstance를 정확히 하나 선택
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
    """패밀리 기준점(LocationPoint)을 가져옵니다."""
    location = instance.Location
    if isinstance(location, LocationPoint):
        return location.Point
    return None


def get_bottom_z(instance):
    """
    패밀리 하단 Z 값을 구합니다.

    장비를 상단 면에 닿게 하려면 기준점이 아니라
    패밀리 바닥 높이를 알아야 하므로 BoundingBox.Min.Z를 씁니다.
    """
    try:
        bbox = instance.get_BoundingBox(None)
    except Exception:
        bbox = None

    if bbox is None:
        return None

    try:
        return bbox.Min.Z
    except Exception:
        return None


def get_import_geometry_options():
    """Import geometry를 읽기 위한 옵션입니다."""
    options = Options()
    options.IncludeNonVisibleObjects = True
    options.ComputeReferences = False
    return options


def get_symbol_geometry(geometry_instance):
    """GeometryInstance에서 SymbolGeometry를 안전하게 가져옵니다."""
    try:
        return geometry_instance.GetSymbolGeometry()
    except Exception:
        return None


def get_instance_geometry(geometry_instance):
    """GeometryInstance에서 InstanceGeometry를 안전하게 가져옵니다."""
    try:
        return geometry_instance.GetInstanceGeometry()
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


def transform_solid_if_needed(solid, transform):
    """
    필요하면 solid에 transform을 적용합니다.

    초보자용 설명:
    - import에서 나온 Solid는 완전히 닫힌 덩어리가 아닐 수도 있습니다.
    - 이런 경우 Volume이 0으로 나오기도 하지만,
      face나 bounding box는 여전히 쓸 수 있습니다.
    - 그래서 Volume이 0이라는 이유만으로 버리지 않습니다.
    """
    if solid is None:
        return None

    try:
        if transform is None:
            return solid
        return SolidUtils.CreateTransformed(solid, transform)
    except Exception:
        return solid


def is_usable_solid(solid):
    """
    face나 edge가 하나라도 있으면 배치 후보로 사용할 수 있다고 봅니다.
    """
    if solid is None:
        return False

    try:
        face_count = solid.Faces.Size
    except Exception:
        face_count = 0

    try:
        edge_count = solid.Edges.Size
    except Exception:
        edge_count = 0

    return face_count > 0 or edge_count > 0


def get_bbox_top_z(solid):
    """solid의 BoundingBox 기준 최고 Z를 구합니다."""
    try:
        bbox = solid.GetBoundingBox()
    except Exception:
        bbox = None

    if bbox is None:
        return None

    try:
        return bbox.Max.Z
    except Exception:
        return None


def collect_import_solid_candidates(
    geometry_element,
    current_transform,
    output_items,
    debug_counts,
    branch_name,
    depth,
):
    """
    Import 내부 geometry를 재귀적으로 돌면서 Solid 후보를 모읍니다.

    초보자용 설명:
    - Import Symbol 안에는 geometry가 여러 겹으로 들어갈 수 있습니다.
    - 그래서 GeometryInstance를 만나면 그 안쪽까지 다시 들어가야 합니다.
    - 최종적으로는 Solid 하나마다
      1. top face 후보
      2. bbox 최고 Z
      를 같이 저장해 둡니다.
    """
    if geometry_element is None:
        return

    if depth > debug_counts["maxDepth"]:
        debug_counts["maxDepth"] = depth

    for geometry_object in geometry_element:
        object_type_name = geometry_object.GetType().Name
        debug_counts["typeCounts"][object_type_name] = (
            debug_counts["typeCounts"].get(object_type_name, 0) + 1
        )

        if isinstance(geometry_object, GeometryInstance):
            symbol_transform = current_transform
            try:
                instance_transform = geometry_object.Transform
                if symbol_transform is None:
                    symbol_transform = instance_transform
                else:
                    symbol_transform = symbol_transform.Multiply(instance_transform)
            except Exception:
                pass

            debug_counts["geometryInstanceCount"] += 1

            nested_symbol_geometry = get_symbol_geometry(geometry_object)
            if nested_symbol_geometry is not None:
                debug_counts["symbolGeometryBranchCount"] += 1
                collect_import_solid_candidates(
                    nested_symbol_geometry,
                    symbol_transform,
                    output_items,
                    debug_counts,
                    "symbol",
                    depth + 1,
                )

            nested_instance_geometry = get_instance_geometry(geometry_object)
            if nested_instance_geometry is not None:
                debug_counts["instanceGeometryBranchCount"] += 1
                # InstanceGeometry는 이미 인스턴스 변환이 반영된 경우가 많아서
                # 같은 transform을 다시 적용하면 좌표가 두 번 이동할 수 있습니다.
                collect_import_solid_candidates(
                    nested_instance_geometry,
                    current_transform,
                    output_items,
                    debug_counts,
                    "instance",
                    depth + 1,
                )
            continue

        if not isinstance(geometry_object, Solid):
            continue

        debug_counts["solidCountRaw"] += 1
        solid = transform_solid_if_needed(geometry_object, current_transform)
        if solid is None or not is_usable_solid(solid):
            debug_counts["emptyOrInvalidSolidCount"] += 1
            continue

        debug_counts["solidCount"] += 1
        bbox_top_z = get_bbox_top_z(solid)
        best_face = None
        best_face_z = None
        top_face_count = 0

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

            # 위쪽을 향하는 면만 top face 후보로 봅니다.
            if normal.Z < 0.7:
                continue

            try:
                face_bbox = face.GetBoundingBox()
                uv_center = (face_bbox.Min + face_bbox.Max) * 0.5
                sample_point = face.Evaluate(uv_center)
                sample_z = sample_point.Z
            except Exception:
                sample_point = None
                sample_z = None

            top_face_count += 1

            if best_face_z is None or (sample_z is not None and sample_z > best_face_z):
                best_face = face
                best_face_z = sample_z

        output_items.append(
            {
                "solid": solid,
                "bboxTopZ": bbox_top_z,
                "topFace": best_face,
                "topFaceZ": best_face_z,
                "topFaceCount": top_face_count,
                "branchName": branch_name,
            }
        )


def build_target_point_from_solid_candidate(source_point, candidate):
    """
    패밀리 위치를 기준으로 가장 적절한 상단 목표점을 계산합니다.

    우선순위:
    1. top face가 있으면 source_point를 그 face에 투영
    2. 실패하면 same XY + bboxTopZ
    """
    top_face = candidate.get("topFace")
    bbox_top_z = candidate.get("bboxTopZ")
    target_point = None
    mode = None

    if top_face is not None:
        try:
            projection = top_face.Project(source_point)
        except Exception:
            projection = None

        if projection is not None:
            try:
                target_point = projection.XYZPoint
            except Exception:
                target_point = None

        if target_point is not None:
            mode = "topFaceProjection"

    if target_point is None and bbox_top_z is not None:
        try:
            target_point = XYZ(source_point.X, source_point.Y, bbox_top_z)
            mode = "solidTopZFallback"
        except Exception:
            target_point = None

    if target_point is None:
        return None, None, None

    try:
        dx = target_point.X - source_point.X
        dy = target_point.Y - source_point.Y
        distance = (dx ** 2 + dy ** 2) ** 0.5
    except Exception:
        distance = None

    return target_point, distance, mode


def find_nearest_top_candidate(source_point, candidates):
    """여러 solid 후보 중에서 XY 기준으로 가장 가까운 상단 목표점을 찾습니다."""
    best_match = None
    best_distance = None

    for index, candidate in enumerate(candidates):
        target_point, distance, mode = build_target_point_from_solid_candidate(source_point, candidate)
        if target_point is None or distance is None:
            continue

        if best_distance is None or distance < best_distance:
            best_distance = distance
            best_match = {
                "index": index,
                "candidate": candidate,
                "targetPoint": target_point,
                "distance": distance,
                "mode": mode,
            }

    return best_match


def make_result(
    selected_family_count=0,
    selected_import_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message="",
    unhandled_error=None,
    host_summary=None,
    invalid_source_ids=None,
    candidate_summary=None,
):
    """Watch에 보기 좋은 결과 딕셔너리를 만듭니다."""
    return {
        "selectedFamilyCount": selected_family_count,
        "selectedImportCount": selected_import_count,
        "movedIds": moved_ids or [],
        "moveDetails": move_details or [],
        "skipped": skipped or [],
        "hostSummary": host_summary or {},
        "candidateSummary": candidate_summary or {},
        "invalidSourceIds": invalid_source_ids or [],
        "message": message,
        "unhandledError": unhandled_error,
    }


def main():
    max_distance_internal = to_internal_mm(to_float(max_distance_mm, 5000.0))

    selected_elements = get_selected_elements()
    families, imports = split_selection(selected_elements)

    invalid_source_ids = []
    valid_families = []

    for family in families:
        if get_instance_point(family) is None:
            invalid_source_ids.append(safe_int_id(family))
            continue
        valid_families.append(family)

    if len(imports) != 1:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=len(imports),
            invalid_source_ids=invalid_source_ids,
            message="Select exactly one Import Symbol and one or more family instances.",
        )

    if not valid_families:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=1,
            invalid_source_ids=invalid_source_ids,
            message="No valid family instances with a LocationPoint were selected.",
        )

    import_element = imports[0]
    geometry_options = get_import_geometry_options()

    try:
        geometry_element = import_element.get_Geometry(geometry_options)
    except Exception:
        geometry_element = None

    candidates = []
    debug_counts = {
        "typeCounts": {},
        "geometryInstanceCount": 0,
        "symbolGeometryBranchCount": 0,
        "instanceGeometryBranchCount": 0,
        "solidCountRaw": 0,
        "solidCount": 0,
        "emptyOrInvalidSolidCount": 0,
        "maxDepth": 0,
    }
    collect_import_solid_candidates(
        geometry_element,
        None,
        candidates,
        debug_counts,
        "root",
        0,
    )

    total_top_face_count = 0
    fallback_candidate_count = 0
    symbol_branch_candidate_count = 0
    instance_branch_candidate_count = 0
    for item in candidates:
        total_top_face_count += item.get("topFaceCount", 0)
        if item.get("bboxTopZ") is not None:
            fallback_candidate_count += 1
        if item.get("branchName") == "symbol":
            symbol_branch_candidate_count += 1
        elif item.get("branchName") == "instance":
            instance_branch_candidate_count += 1

    host_summary = {
        "selectedHostId": safe_int_id(import_element),
        "selectedHostType": "ImportInstance",
    }

    candidate_summary = {
        "solidCount": len(candidates),
        "solidCountRaw": debug_counts["solidCountRaw"],
        "topFaceCount": total_top_face_count,
        "solidTopFallbackCount": fallback_candidate_count,
        "symbolBranchSolidCount": symbol_branch_candidate_count,
        "instanceBranchSolidCount": instance_branch_candidate_count,
        "geometryInstanceCount": debug_counts["geometryInstanceCount"],
        "symbolGeometryBranchCount": debug_counts["symbolGeometryBranchCount"],
        "instanceGeometryBranchCount": debug_counts["instanceGeometryBranchCount"],
        "emptyOrInvalidSolidCount": debug_counts["emptyOrInvalidSolidCount"],
        "maxDepth": debug_counts["maxDepth"],
        "typeCounts": debug_counts["typeCounts"],
        "maxSearchDistanceMm": round(from_internal_mm(max_distance_internal), 2),
    }

    if not candidates:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=1,
            host_summary=host_summary,
            candidate_summary=candidate_summary,
            invalid_source_ids=invalid_source_ids,
            message="No usable Solid candidates were found in the selected Import Symbol.",
        )

    moved_ids = []
    move_details = []
    skipped = []

    TransactionManager.Instance.EnsureInTransaction(doc)
    try:
        for family in valid_families:
            source_point = get_instance_point(family)
            bottom_z = get_bottom_z(family)

            if source_point is None or bottom_z is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "reason": "The family does not expose a usable point or bounding box.",
                    }
                )
                continue

            nearest_match = find_nearest_top_candidate(source_point, candidates)
            if nearest_match is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "reason": "No usable top target was found in the selected Import Symbol.",
                    }
                )
                continue

            target_point = nearest_match["targetPoint"]
            distance = nearest_match["distance"]
            mode = nearest_match["mode"]
            candidate_index = nearest_match["index"]
            candidate = nearest_match["candidate"]

            if distance > max_distance_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(import_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "candidateIndex": candidate_index,
                        "mode": mode,
                        "reason": "The nearest top target is outside the search distance.",
                    }
                )
                continue

            delta_z = target_point.Z - bottom_z
            move_vector = XYZ(0, 0, delta_z)

            sub_transaction = SubTransaction(doc)
            try:
                sub_transaction.Start()
                ElementTransformUtils.MoveElement(doc, family.Id, move_vector)
                sub_transaction.Commit()
            except Exception as move_error:
                try:
                    sub_transaction.RollBack()
                except Exception:
                    pass

                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(import_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "candidateIndex": candidate_index,
                        "mode": mode,
                        "reason": "MoveElement failed.",
                        "error": str(move_error),
                    }
                )
                continue

            moved_ids.append(safe_int_id(family))
            move_details.append(
                {
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family),
                    "hostId": safe_int_id(import_element),
                    "hostType": "ImportInstance",
                    "sourcePoint": xyz_to_list(source_point),
                    "targetPoint": xyz_to_list(target_point),
                    "distanceMm": round(from_internal_mm(distance), 2),
                    "movedDeltaZMm": round(from_internal_mm(delta_z), 2),
                    "candidateIndex": candidate_index,
                    "mode": mode,
                    "candidateTopFaceCount": candidate.get("topFaceCount", 0),
                    "candidateBBoxTopZ": round(from_internal_mm(candidate.get("bboxTopZ")), 2)
                    if candidate.get("bboxTopZ") is not None
                    else None,
                    "status": "movedOnZOnly",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=1,
            moved_ids=moved_ids,
            move_details=move_details,
            skipped=skipped,
            host_summary=host_summary,
            candidate_summary=candidate_summary,
            invalid_source_ids=invalid_source_ids,
            message="The run stopped because of an unexpected error.",
            unhandled_error=str(unhandled_error),
        )
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    return make_result(
        selected_family_count=len(families),
        selected_import_count=1,
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        host_summary=host_summary,
        candidate_summary=candidate_summary,
        invalid_source_ids=invalid_source_ids,
        message="Completed. The selected family instances were moved in Z to the nearest top target in the selected Import Symbol.",
    )


OUT = main()
