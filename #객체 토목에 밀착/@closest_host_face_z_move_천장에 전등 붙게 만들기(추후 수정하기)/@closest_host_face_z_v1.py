import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    BuiltInCategory,
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

# IN[0]: Legacy input kept for Dynamo compatibility. XY search distance is not enforced.
max_distance_mm = IN[0] if len(IN) > 0 else 5000

# Candidates near the family XY within this distance are preferred.
local_xy_tolerance_mm = 300

# Ignore faces that are effectively already attached at this Z distance.
min_vertical_gap_mm = 1

# Safety limit for vertical move. If the computed target is too far away in Z,
# we skip it instead of moving the family to an obviously wrong elevation.
max_vertical_move_mm = 5000


def to_float(value, default_value):
    try:
        return float(value)
    except Exception:
        return default_value


def to_internal_mm(value_in_mm):
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertToInternalUnits(value_in_mm, UnitTypeId.Millimeters)
    except Exception:
        return float(value_in_mm) / 304.8


def from_internal_mm(value_in_internal_units):
    try:
        from Autodesk.Revit.DB import UnitTypeId

        return UnitUtils.ConvertFromInternalUnits(
            value_in_internal_units,
            UnitTypeId.Millimeters,
        )
    except Exception:
        return float(value_in_internal_units) * 304.8


def safe_int_id(element):
    try:
        return element.Id.IntegerValue
    except Exception:
        return None


def xyz_to_list(point):
    if point is None:
        return None

    try:
        return [round(point.X, 6), round(point.Y, 6), round(point.Z, 6)]
    except Exception:
        return None


def get_symbol_label(instance):
    try:
        symbol = instance.Symbol
        return "{0} : {1}".format(symbol.Family.Name, symbol.Name)
    except Exception:
        return "UnknownFamily : UnknownType"


def get_selected_elements():
    if uidoc is None:
        return []
    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def split_selection(elements):
    """
    User workflow:
    - Select one or more FamilyInstance elements to move.
    - Select exactly one host element.
    - Host can be one ImportInstance or one Generic Model FamilyInstance.
    """
    families = []
    hosts = []

    for element in elements:
        if isinstance(element, ImportInstance):
            hosts.append(element)
        elif is_generic_model_instance(element):
            hosts.append(element)
        elif isinstance(element, FamilyInstance):
            families.append(element)

    return families, hosts


def is_generic_model_instance(element):
    if not isinstance(element, FamilyInstance):
        return False

    try:
        category = element.Category
        if category is None:
            return False
        return category.Id.IntegerValue == int(BuiltInCategory.OST_GenericModel)
    except Exception:
        return False


def get_host_type_name(element):
    if isinstance(element, ImportInstance):
        return "ImportInstance"
    if is_generic_model_instance(element):
        return "GenericModel"
    return type(element).__name__


def get_instance_point(instance):
    location = instance.Location
    if isinstance(location, LocationPoint):
        return location.Point
    return None


def get_instance_vertical_bounds(instance):
    try:
        bbox = instance.get_BoundingBox(None)
    except Exception:
        bbox = None

    if bbox is None:
        return None, None

    try:
        return bbox.Min.Z, bbox.Max.Z
    except Exception:
        return None, None


def get_host_geometry_options():
    options = Options()
    options.IncludeNonVisibleObjects = True
    options.ComputeReferences = False
    return options


def get_symbol_geometry(geometry_instance):
    try:
        return geometry_instance.GetSymbolGeometry()
    except Exception:
        return None


def get_instance_geometry(geometry_instance):
    try:
        return geometry_instance.GetInstanceGeometry()
    except Exception:
        return None


def normalize_xyz(vector):
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
    Imported solids may be open shells with zero volume.
    We still keep them if they expose faces or edges.
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


def get_bbox_bottom_z(solid):
    try:
        bbox = solid.GetBoundingBox()
    except Exception:
        bbox = None

    if bbox is None:
        return None

    try:
        return bbox.Min.Z
    except Exception:
        return None


def collect_host_solid_candidates(
    geometry_element,
    current_transform,
    output_items,
    debug_counts,
    branch_name,
    depth,
):
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
            debug_counts["geometryInstanceCount"] += 1

            symbol_transform = current_transform
            try:
                instance_transform = geometry_object.Transform
                if symbol_transform is None:
                    symbol_transform = instance_transform
                else:
                    symbol_transform = symbol_transform.Multiply(instance_transform)
            except Exception:
                pass

            nested_symbol_geometry = get_symbol_geometry(geometry_object)
            if nested_symbol_geometry is not None:
                debug_counts["symbolGeometryBranchCount"] += 1
                collect_host_solid_candidates(
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
                # InstanceGeometry often already includes instance placement.
                collect_host_solid_candidates(
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
        bbox_bottom_z = get_bbox_bottom_z(solid)
        top_face = None
        top_face_z = None
        horizontal_face_count = 0
        top_face_count = 0
        bottom_face_count = 0
        horizontal_faces = []
        top_faces = []

        try:
            faces = solid.Faces
        except Exception:
            faces = []

        for face in faces:
            if not isinstance(face, PlanarFace):
                continue

            normal = normalize_xyz(face.FaceNormal)
            if normal is None or abs(normal.Z) < 0.7:
                continue

            try:
                face_bbox = face.GetBoundingBox()
                uv_center = (face_bbox.Min + face_bbox.Max) * 0.5
                sample_point = face.Evaluate(uv_center)
                sample_z = sample_point.Z
            except Exception:
                sample_z = None

            horizontal_face_count += 1
            face_role = "top" if normal.Z >= 0 else "bottom"
            horizontal_faces.append(
                {
                    "face": face,
                    "role": face_role,
                    "normalZ": normal.Z,
                    "sampleZ": sample_z,
                }
            )

            if face_role == "top":
                top_face_count += 1
                top_faces.append(face)
                if top_face_z is None or (sample_z is not None and sample_z > top_face_z):
                    top_face = face
                    top_face_z = sample_z
            else:
                bottom_face_count += 1

        output_items.append(
            {
                "solid": solid,
                "bboxTopZ": bbox_top_z,
                "bboxBottomZ": bbox_bottom_z,
                "topFace": top_face,
                "topFaceZ": top_face_z,
                "horizontalFaceCount": horizontal_face_count,
                "topFaceCount": top_face_count,
                "bottomFaceCount": bottom_face_count,
                "horizontalFaces": horizontal_faces,
                "topFaces": top_faces,
                "branchName": branch_name,
            }
        )


def get_closest_point_on_segment_2d(px, py, ax, ay, bx, by):
    """2D 선분 위에서 점에 가장 가까운 점을 구합니다."""
    abx = bx - ax
    aby = by - ay
    length_sq = abx * abx + aby * aby

    if length_sq <= 1e-12:
        return ax, ay, 0.0

    t = ((px - ax) * abx + (py - ay) * aby) / length_sq
    if t < 0.0:
        t = 0.0
    elif t > 1.0:
        t = 1.0

    return ax + abx * t, ay + aby * t, t


def get_triangle_barycentric_2d(px, py, p1, p2, p3):
    """XY 평면 기준 삼각형 barycentric 좌표를 계산합니다."""
    x1, y1 = p1.X, p1.Y
    x2, y2 = p2.X, p2.Y
    x3, y3 = p3.X, p3.Y

    denom = ((y2 - y3) * (x1 - x3)) + ((x3 - x2) * (y1 - y3))
    if abs(denom) <= 1e-12:
        return None

    w1 = (((y2 - y3) * (px - x3)) + ((x3 - x2) * (py - y3))) / denom
    w2 = (((y3 - y1) * (px - x3)) + ((x1 - x3) * (py - y3))) / denom
    w3 = 1.0 - w1 - w2
    return w1, w2, w3


def get_local_point_from_triangulated_face(face, source_point, local_xy_tolerance_internal):
    """
    Horizontal face triangulation is used to find a local point at the current XY.

    우선:
    1. source XY가 삼각형 XY 내부에 들어가는지 확인
    2. 내부에 있으면 barycentric으로 Z 보간
    3. 내부 삼각형이 없으면 가장 가까운 삼각형 변의 점을 사용
    """
    try:
        mesh = face.Triangulate()
    except Exception:
        mesh = None

    if mesh is None:
        return None

    px = source_point.X
    py = source_point.Y
    best_inside_point = None
    best_inside_z = None
    best_edge_point = None
    best_edge_distance_sq = None

    try:
        triangle_count = mesh.NumTriangles
    except Exception:
        triangle_count = 0

    for triangle_index in range(triangle_count):
        try:
            triangle = mesh.get_Triangle(triangle_index)
            p1 = triangle.get_Vertex(0)
            p2 = triangle.get_Vertex(1)
            p3 = triangle.get_Vertex(2)
        except Exception:
            continue

        barycentric = get_triangle_barycentric_2d(px, py, p1, p2, p3)
        if barycentric is not None:
            w1, w2, w3 = barycentric
            tolerance = 1e-6
            if w1 >= -tolerance and w2 >= -tolerance and w3 >= -tolerance:
                z_value = (w1 * p1.Z) + (w2 * p2.Z) + (w3 * p3.Z)
                if best_inside_z is None or z_value > best_inside_z:
                    best_inside_z = z_value
                    best_inside_point = XYZ(px, py, z_value)

        segment_checks = [
            (p1, p2),
            (p2, p3),
            (p3, p1),
        ]
        for start_point, end_point in segment_checks:
            qx, qy, t = get_closest_point_on_segment_2d(
                px,
                py,
                start_point.X,
                start_point.Y,
                end_point.X,
                end_point.Y,
            )
            qz = start_point.Z + (end_point.Z - start_point.Z) * t
            dx = qx - px
            dy = qy - py
            distance_sq = dx * dx + dy * dy
            if best_edge_distance_sq is None or distance_sq < best_edge_distance_sq:
                best_edge_distance_sq = distance_sq
                best_edge_point = XYZ(qx, qy, qz)

    if best_inside_point is not None:
        return best_inside_point

    if best_edge_point is not None and best_edge_distance_sq is not None:
        if best_edge_distance_sq <= (local_xy_tolerance_internal * local_xy_tolerance_internal):
            return best_edge_point

    return best_edge_point


def build_target_point_from_solid_candidate(
    source_point,
    family_bottom_z,
    candidate,
    local_xy_tolerance_internal,
    min_vertical_gap_internal,
):
    """
    Search all horizontal faces in a solid and keep the face whose Z is closest
    to the family's bottom reference.
    """
    horizontal_faces = candidate.get("horizontalFaces") or []
    bbox_top_z = candidate.get("bboxTopZ")
    bbox_bottom_z = candidate.get("bboxBottomZ")
    target_point = None
    mode = None
    best_face_distance = None
    best_vertical_delta = None
    best_contact_side = None
    best_host_face_role = None
    best_is_local = None

    for face_info in horizontal_faces:
        face = face_info.get("face")
        face_role = face_info.get("role")
        candidate_point = get_local_point_from_triangulated_face(
            face,
            source_point,
            local_xy_tolerance_internal,
        )
        candidate_mode = "horizontalFaceTriangulatedLocal"

        if candidate_point is None:
            continue

        try:
            dx = candidate_point.X - source_point.X
            dy = candidate_point.Y - source_point.Y
            candidate_distance = (dx ** 2 + dy ** 2) ** 0.5
        except Exception:
            candidate_distance = None

        if candidate_distance is None:
            continue

        contact_side = None
        vertical_delta = None

        if family_bottom_z is not None:
            vertical_delta = candidate_point.Z - family_bottom_z
            contact_side = "familyBottom"

        if vertical_delta is None:
            continue

        if abs(vertical_delta) < min_vertical_gap_internal:
            continue

        is_local = candidate_distance <= local_xy_tolerance_internal

        should_replace = False
        if best_is_local is None or (is_local and not best_is_local):
            should_replace = True
        elif is_local == best_is_local:
            if best_vertical_delta is None or abs(vertical_delta) < abs(best_vertical_delta):
                should_replace = True
            elif abs(vertical_delta) == abs(best_vertical_delta):
                if best_face_distance is None or candidate_distance < best_face_distance:
                    should_replace = True

        if should_replace:
            best_is_local = is_local
            best_face_distance = candidate_distance
            best_vertical_delta = vertical_delta
            best_contact_side = contact_side
            best_host_face_role = face_role
            target_point = candidate_point
            mode = candidate_mode

    if target_point is None and candidate.get("horizontalFaceCount", 0) == 0:
        fallback_planes = []
        if bbox_top_z is not None:
            fallback_planes.append(("bboxTop", bbox_top_z))
        if bbox_bottom_z is not None:
            fallback_planes.append(("bboxBottom", bbox_bottom_z))

        for plane_role, plane_z in fallback_planes:
            plane_delta = None
            plane_contact_side = None

            if family_bottom_z is not None:
                plane_delta = plane_z - family_bottom_z
                plane_contact_side = "familyBottom"

            if plane_delta is None:
                continue

            if best_vertical_delta is None or abs(plane_delta) < abs(best_vertical_delta):
                should_replace = True
                best_vertical_delta = plane_delta
                best_contact_side = plane_contact_side
                best_host_face_role = plane_role
                target_point = XYZ(source_point.X, source_point.Y, plane_z)
                best_face_distance = 0.0
                best_is_local = True
                mode = "solidHorizontalFallback"

    if target_point is None:
        return None, None, None, None, None

    try:
        dx = target_point.X - source_point.X
        dy = target_point.Y - source_point.Y
        distance = (dx ** 2 + dy ** 2) ** 0.5
    except Exception:
        distance = None

    return (
        target_point,
        distance,
        mode,
        best_vertical_delta,
        {
            "contactSide": best_contact_side,
            "hostFaceRole": best_host_face_role,
        },
    )


def get_candidate_rank(candidate, mode, distance, local_xy_tolerance_internal):
    """
    Prefer:
    1. instance branch + direct face projection + close XY
    2. instance branch + triangulated local point + close XY
    3. symbol branch + direct face projection + close XY
    4. symbol branch + triangulated local point + close XY
    5. instance branch + direct face projection
    6. instance branch + triangulated local point
    7. symbol branch + direct face projection
    8. symbol branch + triangulated local point
    9. instance branch fallback
    10. symbol branch fallback
    """
    branch_name = candidate.get("branchName")
    is_local = distance is not None and distance <= local_xy_tolerance_internal

    if branch_name == "instance" and mode == "horizontalFaceProjection" and is_local:
        return 1
    if branch_name == "instance" and mode == "horizontalFaceTriangulatedLocal" and is_local:
        return 2
    if branch_name == "symbol" and mode == "horizontalFaceProjection" and is_local:
        return 3
    if branch_name == "symbol" and mode == "horizontalFaceTriangulatedLocal" and is_local:
        return 4
    if branch_name == "instance" and mode == "horizontalFaceProjection":
        return 5
    if branch_name == "instance" and mode == "horizontalFaceTriangulatedLocal":
        return 6
    if branch_name == "symbol" and mode == "horizontalFaceProjection":
        return 7
    if branch_name == "symbol" and mode == "horizontalFaceTriangulatedLocal":
        return 8
    if branch_name == "instance":
        return 9
    if branch_name == "symbol":
        return 10
    return 11


def find_nearest_host_face_candidate(
    source_point,
    family_bottom_z,
    candidates,
    local_xy_tolerance_internal,
    min_vertical_gap_internal,
):
    best_match = None
    best_vertical_delta = None
    best_distance = None
    best_rank = None

    for index, candidate in enumerate(candidates):
        (
            target_point,
            distance,
            mode,
            vertical_delta,
            contact_info,
        ) = build_target_point_from_solid_candidate(
            source_point,
            family_bottom_z,
            candidate,
            local_xy_tolerance_internal,
            min_vertical_gap_internal,
        )
        if target_point is None or distance is None or vertical_delta is None:
            continue

        rank = get_candidate_rank(candidate, mode, distance, local_xy_tolerance_internal)

        should_replace = False
        if best_vertical_delta is None or abs(vertical_delta) < abs(best_vertical_delta):
            should_replace = True
        elif abs(vertical_delta) == abs(best_vertical_delta):
            if best_rank is None or rank < best_rank:
                should_replace = True
            elif rank == best_rank and (best_distance is None or distance < best_distance):
                should_replace = True

        if should_replace:
            best_vertical_delta = vertical_delta
            best_rank = rank
            best_distance = distance
            best_match = {
                "index": index,
                "candidate": candidate,
                "targetPoint": target_point,
                "distance": distance,
                "mode": mode,
                "rank": rank,
                "verticalDelta": vertical_delta,
                "contactSide": contact_info.get("contactSide") if contact_info else None,
                "hostFaceRole": contact_info.get("hostFaceRole") if contact_info else None,
            }

    return best_match


def get_host_vertical_range(candidates):
    host_top_z = None
    host_bottom_z = None

    for candidate in candidates:
        candidate_top_z = candidate.get("bboxTopZ")
        candidate_bottom_z = candidate.get("bboxBottomZ")

        if candidate_top_z is not None:
            if host_top_z is None or candidate_top_z > host_top_z:
                host_top_z = candidate_top_z

        if candidate_bottom_z is not None:
            if host_bottom_z is None or candidate_bottom_z < host_bottom_z:
                host_bottom_z = candidate_bottom_z

    return host_bottom_z, host_top_z


def make_result(
    selected_family_count=0,
    selected_import_count=0,
    selected_host_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message="",
    unhandled_error=None,
    host_summary=None,
    invalid_source_ids=None,
    candidate_summary=None,
):
    return {
        "selectedFamilyCount": selected_family_count,
        "selectedImportCount": selected_import_count,
        "selectedHostCount": selected_host_count,
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
    local_xy_tolerance_internal = to_internal_mm(local_xy_tolerance_mm)
    min_vertical_gap_internal = to_internal_mm(min_vertical_gap_mm)
    max_vertical_move_internal = to_internal_mm(max_vertical_move_mm)

    selected_elements = get_selected_elements()
    families, hosts = split_selection(selected_elements)

    invalid_source_ids = []
    valid_families = []

    for family in families:
        if get_instance_point(family) is None:
            invalid_source_ids.append(safe_int_id(family))
            continue
        valid_families.append(family)

    selected_import_count = len(
        [element for element in hosts if isinstance(element, ImportInstance)]
    )

    if len(hosts) != 1:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=selected_import_count,
            selected_host_count=len(hosts),
            invalid_source_ids=invalid_source_ids,
            message="Select exactly one host element (Import Symbol or Generic Model) and one or more family instances.",
        )

    if not valid_families:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=selected_import_count,
            selected_host_count=1,
            invalid_source_ids=invalid_source_ids,
            message="No valid family instances with a LocationPoint were selected.",
        )

    host_element = hosts[0]
    host_type_name = get_host_type_name(host_element)
    geometry_options = get_host_geometry_options()

    try:
        geometry_element = host_element.get_Geometry(geometry_options)
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
    collect_host_solid_candidates(
        geometry_element,
        None,
        candidates,
        debug_counts,
        "root",
        0,
    )

    total_horizontal_face_count = 0
    total_top_face_count = 0
    total_bottom_face_count = 0
    fallback_candidate_count = 0
    symbol_branch_candidate_count = 0
    instance_branch_candidate_count = 0
    for item in candidates:
        total_horizontal_face_count += item.get("horizontalFaceCount", 0)
        total_top_face_count += item.get("topFaceCount", 0)
        total_bottom_face_count += item.get("bottomFaceCount", 0)
        if item.get("bboxTopZ") is not None:
            fallback_candidate_count += 1
        if item.get("branchName") == "symbol":
            symbol_branch_candidate_count += 1
        elif item.get("branchName") == "instance":
            instance_branch_candidate_count += 1

    host_bottom_z, host_top_z = get_host_vertical_range(candidates)

    host_summary = {
        "selectedHostId": safe_int_id(host_element),
        "selectedHostType": host_type_name,
        "hostBottomZMm": round(from_internal_mm(host_bottom_z), 2)
        if host_bottom_z is not None
        else None,
        "hostTopZMm": round(from_internal_mm(host_top_z), 2)
        if host_top_z is not None
        else None,
    }

    candidate_summary = {
        "solidCount": len(candidates),
        "solidCountRaw": debug_counts["solidCountRaw"],
        "horizontalFaceCount": total_horizontal_face_count,
        "topFaceCount": total_top_face_count,
        "bottomFaceCount": total_bottom_face_count,
        "solidHorizontalFallbackCount": fallback_candidate_count,
        "symbolBranchSolidCount": symbol_branch_candidate_count,
        "instanceBranchSolidCount": instance_branch_candidate_count,
        "geometryInstanceCount": debug_counts["geometryInstanceCount"],
        "symbolGeometryBranchCount": debug_counts["symbolGeometryBranchCount"],
        "instanceGeometryBranchCount": debug_counts["instanceGeometryBranchCount"],
        "emptyOrInvalidSolidCount": debug_counts["emptyOrInvalidSolidCount"],
        "maxDepth": debug_counts["maxDepth"],
        "typeCounts": debug_counts["typeCounts"],
        "localXYToleranceMm": local_xy_tolerance_mm,
        "minVerticalGapMm": min_vertical_gap_mm,
        "maxVerticalMoveMm": max_vertical_move_mm,
        "legacyInputMaxSearchDistanceMm": round(
            from_internal_mm(to_internal_mm(to_float(max_distance_mm, 5000.0))), 2
        ),
        "xySearchDistanceEnforced": False,
    }

    if not candidates:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=selected_import_count,
            selected_host_count=1,
            host_summary=host_summary,
            candidate_summary=candidate_summary,
            invalid_source_ids=invalid_source_ids,
            message="No usable Solid candidates were found in the selected host element.",
        )

    moved_ids = []
    move_details = []
    skipped = []

    TransactionManager.Instance.EnsureInTransaction(doc)
    try:
        for family in valid_families:
            source_point = get_instance_point(family)
            family_bottom_z, family_top_z = get_instance_vertical_bounds(family)

            if source_point is None or family_bottom_z is None or family_top_z is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "reason": "The family does not expose a usable point or bounding box.",
                    }
                )
                continue

            nearest_match = find_nearest_host_face_candidate(
                source_point,
                family_bottom_z,
                candidates,
                local_xy_tolerance_internal,
                min_vertical_gap_internal,
            )
            if nearest_match is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "reason": "No usable host face was found after excluding same-Z faces in the selected host element.",
                    }
                )
                continue

            target_point = nearest_match["targetPoint"]
            distance = nearest_match["distance"]
            mode = nearest_match["mode"]
            rank = nearest_match["rank"]
            candidate_index = nearest_match["index"]
            candidate = nearest_match["candidate"]
            vertical_delta = nearest_match["verticalDelta"]
            contact_side = nearest_match["contactSide"]
            host_face_role = nearest_match["hostFaceRole"]

            if abs(vertical_delta) > max_vertical_move_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(host_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "verticalDeltaMm": round(from_internal_mm(vertical_delta), 2),
                        "candidateIndex": candidate_index,
                        "mode": mode,
                        "rank": rank,
                        "branchName": candidate.get("branchName"),
                        "reason": "The target height is too far away in Z, so the move was skipped for safety.",
                    }
                )
                continue

            delta_z = vertical_delta
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
                        "hostId": safe_int_id(host_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "candidateIndex": candidate_index,
                        "mode": mode,
                        "rank": rank,
                        "branchName": candidate.get("branchName"),
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
                    "hostId": safe_int_id(host_element),
                    "hostType": host_type_name,
                    "sourcePoint": xyz_to_list(source_point),
                    "targetPoint": xyz_to_list(target_point),
                    "sourceBottomZMm": round(from_internal_mm(family_bottom_z), 2),
                    "sourceTopZMm": round(from_internal_mm(family_top_z), 2),
                    "distanceMm": round(from_internal_mm(distance), 2),
                    "movedDeltaZMm": round(from_internal_mm(delta_z), 2),
                    "verticalDeltaMm": round(from_internal_mm(vertical_delta), 2),
                    "candidateIndex": candidate_index,
                    "mode": mode,
                    "rank": rank,
                    "contactSide": contact_side,
                    "hostFaceRole": host_face_role,
                    "branchName": candidate.get("branchName"),
                    "candidateHorizontalFaceCount": candidate.get("horizontalFaceCount", 0),
                    "candidateTopFaceCount": candidate.get("topFaceCount", 0),
                    "candidateBottomFaceCount": candidate.get("bottomFaceCount", 0),
                    "candidateBBoxTopZ": round(from_internal_mm(candidate.get("bboxTopZ")), 2)
                    if candidate.get("bboxTopZ") is not None
                    else None,
                    "candidateBBoxBottomZ": round(from_internal_mm(candidate.get("bboxBottomZ")), 2)
                    if candidate.get("bboxBottomZ") is not None
                    else None,
                    "status": "movedOnZOnly",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            selected_import_count=selected_import_count,
            selected_host_count=1,
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
        selected_import_count=selected_import_count,
        selected_host_count=1,
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        host_summary=host_summary,
        candidate_summary=candidate_summary,
        invalid_source_ids=invalid_source_ids,
        message="Completed. The selected family instances were moved in Z to the nearest host face in the selected host element.",
    )


OUT = main()
