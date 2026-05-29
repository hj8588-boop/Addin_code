import clr
from System.Collections.Generic import List

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    BuiltInCategory,
    BuiltInParameter,
    CurveElement,
    CurveLoop,
    DirectShape,
    ElementId,
    ElementTransformUtils,
    Face,
    FamilyInstance,
    FilteredElementCollector,
    GeometryObject,
    GeometryCreationUtilities,
    GeometryInstance,
    Group,
    ImportInstance,
    Line,
    LocationPoint,
    Options,
    Plane,
    PlanarFace,
    RevitLinkInstance,
    SketchPlane,
    Solid,
    SubTransaction,
    TessellatedFace,
    TessellatedShapeBuilder,
    TessellatedShapeBuilderFallback,
    TessellatedShapeBuilderTarget,
    Transform,
    UnitUtils,
    XYZ,
)

clr.AddReference("RevitAPIUI")
from Autodesk.Revit.UI.Selection import ObjectType

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
uidoc = uiapp.ActiveUIDocument if uiapp else None

min_vertical_gap_mm = 1
max_vertical_move_mm = 5000
face_pick_tolerance_mm = 20
planar_pick_z_tolerance_mm = 50
import_edge_fallback_tolerance_mm = 1000
helper_face_thickness_mm = 1
helper_face_margin_mm = 2000
object_marker_size_mm = 300
object_marker_height_mm = 100
use_fast_mode = True
multi_face_pick_enabled = True
multi_face_global_fallback_tolerance_mm = 5000
face_highlight_curve_marker = "CodexSelectedFaceHighlight"
object_highlight_curve_marker = "CodexSelectedObjectHighlight"


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
        return [
            round(float(point.X), 6),
            round(float(point.Y), 6),
            round(float(point.Z), 6),
        ]
    except Exception:
        return None


def move_point_xy(point, delta_x, delta_y):
    if point is None:
        return None

    try:
        return XYZ(point.X + delta_x, point.Y + delta_y, point.Z)
    except Exception:
        return None


def safe_int_id_from_element_id(element_id):
    try:
        return element_id.IntegerValue
    except Exception:
        return None


def get_symbol_label(instance):
    try:
        if isinstance(instance, Group):
            group_type = doc.GetElement(instance.GetTypeId())
            if group_type is not None:
                return "Group : {0}".format(group_type.Name)
            return "Group"
    except Exception:
        pass

    try:
        symbol = instance.Symbol
        return "{0} : {1}".format(symbol.Family.Name, symbol.Name)
    except Exception:
        return "UnknownFamily : UnknownType"


def get_selected_elements():
    if uidoc is None:
        return []
    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def get_selected_movable_elements():
    elements = []

    for element in get_selected_elements():
        if isinstance(element, (FamilyInstance, Group)):
            elements.append(element)

    return elements


def set_ui_selection(elements):
    if uidoc is None:
        return False

    selection_ids = List[ElementId]()
    seen_ids = set()

    for element in elements or []:
        if element is None:
            continue

        try:
            element_id = element.Id
            integer_id = element_id.IntegerValue
        except Exception:
            continue

        if integer_id in seen_ids:
            continue

        seen_ids.add(integer_id)
        selection_ids.Add(element_id)

    try:
        uidoc.Selection.SetElementIds(selection_ids)
        return True
    except Exception:
        return False


def show_ui_elements(elements):
    if uidoc is None:
        return False

    selection_ids = List[ElementId]()
    seen_ids = set()

    for element in elements or []:
        if element is None:
            continue

        try:
            element_id = element.Id
            integer_id = element_id.IntegerValue
        except Exception:
            continue

        if integer_id in seen_ids:
            continue

        seen_ids.add(integer_id)
        selection_ids.Add(element_id)

    try:
        uidoc.ShowElements(selection_ids)
        return True
    except Exception:
        return False


def refresh_ui_view():
    if uidoc is None:
        return False

    try:
        uidoc.RefreshActiveView()
        return True
    except Exception:
        return False


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


def get_element_geometry_options():
    options = Options()
    options.IncludeNonVisibleObjects = True
    options.ComputeReferences = False
    return options


def iter_solids_from_geometry(geometry_element, current_transform=None):
    if geometry_element is None:
        return

    for geometry_object in geometry_element:
        if isinstance(geometry_object, GeometryInstance):
            geometry_transform = current_transform
            try:
                instance_transform = geometry_object.Transform
            except Exception:
                instance_transform = None

            geometry_transform = multiply_transforms(
                geometry_transform,
                instance_transform,
            )

            try:
                instance_geometry = geometry_object.GetInstanceGeometry()
            except Exception:
                instance_geometry = None

            if instance_geometry is not None:
                for nested_solid, nested_transform in iter_solids_from_geometry(
                    instance_geometry,
                    current_transform,
                ):
                    yield nested_solid, nested_transform

            try:
                symbol_geometry = geometry_object.GetSymbolGeometry()
            except Exception:
                symbol_geometry = None

            if symbol_geometry is not None:
                for nested_solid, nested_transform in iter_solids_from_geometry(
                    symbol_geometry,
                    geometry_transform,
                ):
                    yield nested_solid, nested_transform
            continue

        if isinstance(geometry_object, Solid):
            try:
                if geometry_object.Volume <= 1e-9 and geometry_object.Faces.Size <= 0:
                    continue
            except Exception:
                pass
            yield geometry_object, current_transform


def get_lowest_points_from_solid(solid, transform=None, tolerance=None):
    lowest_points = []
    lowest_z = None

    try:
        faces = solid.Faces
    except Exception:
        faces = []

    for face in faces:
        try:
            mesh = face.Triangulate()
        except Exception:
            mesh = None

        if mesh is None:
            continue

        try:
            triangle_count = mesh.NumTriangles
        except Exception:
            triangle_count = 0

        for triangle_index in range(triangle_count):
            try:
                triangle = mesh.get_Triangle(triangle_index)
            except Exception:
                continue

            for vertex_index in range(3):
                try:
                    world_point = apply_transform_to_point(
                        triangle.get_Vertex(vertex_index),
                        transform,
                    )
                except Exception:
                    world_point = None

                if world_point is None:
                    continue

                if lowest_z is None or world_point.Z < (lowest_z - 1e-9):
                    lowest_z = world_point.Z
                    lowest_points = [world_point]
                    continue

                if tolerance is None:
                    tolerance = to_internal_mm(1.0)

                if abs(world_point.Z - lowest_z) <= tolerance:
                    lowest_points.append(world_point)

    return lowest_points


def get_instance_lowest_points(instance, tolerance_mm=1.0):
    if use_fast_mode:
        return []

    try:
        geometry_element = instance.get_Geometry(get_element_geometry_options())
    except Exception:
        geometry_element = None

    if geometry_element is None:
        return []

    tolerance = to_internal_mm(tolerance_mm)
    all_lowest_points = []
    global_lowest_z = None

    for solid, solid_transform in iter_solids_from_geometry(geometry_element, None):
        solid_points = get_lowest_points_from_solid(
            solid,
            solid_transform,
            tolerance,
        )
        for point in solid_points:
            if global_lowest_z is None or point.Z < (global_lowest_z - 1e-9):
                global_lowest_z = point.Z
                all_lowest_points = [point]
            elif abs(point.Z - global_lowest_z) <= tolerance:
                all_lowest_points.append(point)

    return all_lowest_points


def get_instance_bottom_corners(instance):
    try:
        bbox = instance.get_BoundingBox(None)
    except Exception:
        bbox = None

    if bbox is not None:
        try:
            min_point = bbox.Min
            max_point = bbox.Max
            z_value = min_point.Z
            return [
                XYZ(min_point.X, min_point.Y, z_value),
                XYZ(min_point.X, max_point.Y, z_value),
                XYZ(max_point.X, min_point.Y, z_value),
                XYZ(max_point.X, max_point.Y, z_value),
            ]
        except Exception:
            pass

    lowest_points = get_instance_lowest_points(instance)
    if lowest_points:
        return lowest_points

    return []


def get_instance_bottom_center(instance):
    try:
        bbox = instance.get_BoundingBox(None)
    except Exception:
        bbox = None

    if bbox is not None:
        try:
            min_point = bbox.Min
            max_point = bbox.Max
            return XYZ(
                (min_point.X + max_point.X) / 2.0,
                (min_point.Y + max_point.Y) / 2.0,
                min_point.Z,
            )
        except Exception:
            pass

    lowest_points = get_instance_lowest_points(instance)
    if lowest_points:
        try:
            point_count = float(len(lowest_points))
            avg_x = sum([point.X for point in lowest_points]) / point_count
            avg_y = sum([point.Y for point in lowest_points]) / point_count
            min_z = min([point.Z for point in lowest_points])
            return XYZ(avg_x, avg_y, min_z)
        except Exception:
            pass

    return get_instance_point(instance)


def get_closest_point_on_segment_2d(px, py, ax, ay, bx, by):
    abx = bx - ax
    aby = by - ay
    length_sq = (abx * abx) + (aby * aby)

    if length_sq <= 1e-12:
        return ax, ay, 0.0

    t = ((px - ax) * abx + (py - ay) * aby) / length_sq
    if t < 0.0:
        t = 0.0
    elif t > 1.0:
        t = 1.0

    return ax + (abx * t), ay + (aby * t), t


def get_triangle_barycentric_2d(px, py, p1, p2, p3):
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


def apply_transform_to_point(point, transform):
    if point is None:
        return None

    if transform is None:
        return point

    try:
        return transform.OfPoint(point)
    except Exception:
        return point


def multiply_transforms(first_transform, second_transform):
    if first_transform is None:
        return second_transform
    if second_transform is None:
        return first_transform

    try:
        return first_transform.Multiply(second_transform)
    except Exception:
        return first_transform


def invert_transform(transform):
    if transform is None:
        return None

    try:
        return transform.Inverse
    except Exception:
        return None


def get_world_face_normal(face, transform=None):
    try:
        normal = face.ComputeNormal(None)
    except Exception:
        try:
            normal = face.FaceNormal
        except Exception:
            normal = None

    if normal is None:
        return None

    if transform is not None:
        try:
            normal = transform.OfVector(normal)
        except Exception:
            pass

    try:
        return normal.Normalize()
    except Exception:
        return normal


def is_upward_facing_face(face, transform=None):
    normal = get_world_face_normal(face, transform)
    if normal is None:
        return False

    try:
        return normal.Z >= 0.2
    except Exception:
        return False


def get_projected_point_on_face(face, source_point, transform=None):
    local_source_point = source_point
    inverse_transform = invert_transform(transform)
    if inverse_transform is not None:
        try:
            local_source_point = inverse_transform.OfPoint(source_point)
        except Exception:
            local_source_point = source_point

    try:
        projection = face.Project(local_source_point)
    except Exception:
        projection = None

    if projection is None:
        return None, None

    try:
        projected_point = projection.XYZPoint
    except Exception:
        projected_point = None

    if projected_point is None:
        return None, None

    projected_point = apply_transform_to_point(projected_point, transform)
    if projected_point is None:
        return None, None

    try:
        dx = projected_point.X - source_point.X
        dy = projected_point.Y - source_point.Y
        dz = projected_point.Z - source_point.Z
        distance_3d = ((dx * dx) + (dy * dy) + (dz * dz)) ** 0.5
    except Exception:
        distance_3d = None

    return projected_point, distance_3d


def get_reference_point_on_face(face, reference, transform=None):
    if face is None or reference is None:
        return None

    try:
        global_point = reference.GlobalPoint
    except Exception:
        global_point = None

    if global_point is not None:
        return global_point

    try:
        uv_point = reference.UVPoint
    except Exception:
        uv_point = None

    if uv_point is None:
        return None

    try:
        evaluated_point = face.Evaluate(uv_point)
    except Exception:
        evaluated_point = None

    return apply_transform_to_point(evaluated_point, transform)


def is_precise_selection_mode(selection_mode):
    if selection_mode is None:
        return False

    if selection_mode == "directFaceReference":
        return True

    if selection_mode == "tracedLinkPlanarFace":
        return True

    if selection_mode == "link:pickedPointOnly":
        return True

    if str(selection_mode).startswith("import:"):
        return True

    if str(selection_mode).startswith("link:familyAuto:"):
        return True

    return str(selection_mode).endswith(":inside")


def is_planar_auto_edge_mode(selection_mode):
    if selection_mode is None:
        return False

    selection_text = str(selection_mode)
    return selection_text.startswith("link:auto:") and selection_text.endswith(":edge")


def is_auto_edge_mode(selection_mode):
    if selection_mode is None:
        return False

    selection_text = str(selection_mode)
    return selection_text.startswith("link:auto:") and selection_text.endswith(":edge")


def is_import_selection_mode(selection_mode):
    if selection_mode is None:
        return False

    return str(selection_mode).startswith("import:")


def should_move_to_picked_xy(selection_mode, picked_point=None):
    if selection_mode is None or picked_point is None:
        return False

    selection_text = str(selection_mode)

    if selection_text.startswith("import:"):
        return False

    if selection_text.startswith("link:auto:"):
        return False

    if selection_text.startswith("link:familyAuto:"):
        return False

    return True


def get_preferred_target_point(
    face,
    source_point,
    source_bottom_points=None,
    picked_point=None,
    transform=None,
    selection_mode=None,
    triangle_cache=None,
):
    precise_selection = is_precise_selection_mode(selection_mode)
    auto_edge_selection = is_auto_edge_mode(selection_mode)
    planar_auto_edge_selection = is_planar_auto_edge_mode(selection_mode)
    import_selection = is_import_selection_mode(selection_mode)
    planar_pick_z_tolerance = to_internal_mm(planar_pick_z_tolerance_mm)
    face_sources = []
    if source_point is not None:
        face_sources.append(source_point)

    for candidate_source in source_bottom_points or []:
        if candidate_source is None:
            continue

        is_duplicate = False
        for existing_source in face_sources:
            try:
                if existing_source.DistanceTo(candidate_source) <= 1e-9:
                    is_duplicate = True
                    break
            except Exception:
                pass

        if not is_duplicate:
            face_sources.append(candidate_source)

    if not face_sources:
        face_sources = [source_point]

    if (
        picked_point is not None
        and selection_mode is not None
        and str(selection_mode).startswith("link:pickedPointOnly")
    ):
        try:
            target_point = XYZ(source_point.X, source_point.Y, picked_point.Z)
            dx = picked_point.X - source_point.X
            dy = picked_point.Y - source_point.Y
            xy_distance = ((dx * dx) + (dy * dy)) ** 0.5
            return target_point, xy_distance, "pickedPointZAbsolute"
        except Exception:
            pass

    if import_selection:
        face_candidates = []
        max_xy_distance = 0.0

        for face_source in face_sources:
            target_point, xy_distance = get_local_point_from_face(
                face,
                face_source,
                transform,
                allow_edge_fallback=False,
                triangle_cache=triangle_cache,
            )
            if target_point is None:
                continue

            face_candidates.append(target_point)
            if xy_distance is not None and xy_distance > max_xy_distance:
                max_xy_distance = xy_distance

        if face_candidates:
            best_face_point = get_best_candidate_point_by_z(face_candidates, source_point)
            if best_face_point is not None:
                return best_face_point, max_xy_distance, "faceAtSourceXY"
            return face_candidates[0], max_xy_distance, "faceAtSourceXY"

        edge_candidates = []
        edge_tolerance = to_internal_mm(import_edge_fallback_tolerance_mm)

        for face_source in face_sources:
            target_point, xy_distance = get_local_point_from_face(
                face,
                face_source,
                transform,
                allow_edge_fallback=True,
                triangle_cache=triangle_cache,
            )
            if target_point is None or xy_distance is None:
                continue
            if xy_distance > edge_tolerance:
                continue

            edge_candidates.append((target_point, xy_distance))

        if edge_candidates:
            best_edge_point = None
            best_edge_distance = None
            best_edge_gap = None

            for candidate_point, candidate_distance in edge_candidates:
                try:
                    candidate_gap = abs(candidate_point.Z - source_point.Z)
                except Exception:
                    candidate_gap = None

                if best_edge_point is None:
                    best_edge_point = candidate_point
                    best_edge_distance = candidate_distance
                    best_edge_gap = candidate_gap
                    continue

                if candidate_distance < best_edge_distance:
                    best_edge_point = candidate_point
                    best_edge_distance = candidate_distance
                    best_edge_gap = candidate_gap
                    continue

                if (
                    abs(candidate_distance - best_edge_distance) <= 1e-9
                    and candidate_gap is not None
                    and best_edge_gap is not None
                    and candidate_gap < best_edge_gap
                ):
                    best_edge_point = candidate_point
                    best_edge_distance = candidate_distance
                    best_edge_gap = candidate_gap

            if best_edge_point is not None:
                return best_edge_point, best_edge_distance, "faceEdgeFallbackXY"

        return None, None, None

    if isinstance(face, PlanarFace):
        if precise_selection or planar_auto_edge_selection:
            planar_sources = source_bottom_points or [source_point]
            planar_candidates = []
            planar_distance = 0.0

            for planar_source in planar_sources:
                inside_face_point, _ = get_local_point_from_face(
                    face,
                    planar_source,
                    transform,
                    allow_edge_fallback=False,
                    triangle_cache=triangle_cache,
                )
                if inside_face_point is None:
                    continue

                planar_point, planar_distance_candidate = get_planar_face_point_at_same_xy(
                    face,
                    planar_source,
                    transform,
                )
                if planar_point is not None:
                    planar_candidates.append(planar_point)
                    if planar_distance_candidate is not None and planar_distance_candidate > planar_distance:
                        planar_distance = planar_distance_candidate

            planar_point = get_best_candidate_point_by_z(planar_candidates, source_point)

            if planar_point is not None:
                if planar_auto_edge_selection and picked_point is not None:
                    picked_planar_point, _ = get_planar_face_point_at_same_xy(
                        face,
                        picked_point,
                        transform,
                    )
                    if picked_planar_point is None:
                        planar_point = None
                    else:
                        picked_z_gap = abs(picked_planar_point.Z - picked_point.Z)
                        if picked_z_gap > planar_pick_z_tolerance:
                            planar_point = None

                if planar_point is not None:
                    return planar_point, planar_distance, "planarFaceSameXY"

        local_normal = None
        try:
            local_normal = face.FaceNormal
        except Exception:
            local_normal = None

        world_normal = local_normal
        if local_normal is not None and transform is not None:
            try:
                world_normal = transform.OfVector(local_normal)
            except Exception:
                world_normal = local_normal

        try:
            if (
                (precise_selection or planar_auto_edge_selection)
                and world_normal is not None
                and abs(world_normal.Normalize().Z) >= 0.999
            ):
                plane_origin = apply_transform_to_point(face.Origin, transform)
                if plane_origin is not None:
                    target_point = XYZ(source_point.X, source_point.Y, plane_origin.Z)
                    projected_point, projected_distance = get_projected_point_on_face(
                        face,
                        source_point,
                        transform,
                    )
                    if projected_point is not None and projected_distance is not None:
                        xy_distance = projected_distance
                    else:
                        xy_distance = 0.0
                    return target_point, xy_distance, "horizontalFacePlaneZ"
        except Exception:
            pass

    if precise_selection or auto_edge_selection:
        face_candidates = []
        max_xy_distance = 0.0

        for face_source in face_sources:
            target_point, xy_distance = get_local_point_from_face(
                face,
                face_source,
                transform,
                allow_edge_fallback=False,
                triangle_cache=triangle_cache,
            )
            if target_point is None:
                continue

            face_candidates.append(target_point)
            if xy_distance is not None and xy_distance > max_xy_distance:
                max_xy_distance = xy_distance

        if face_candidates:
            best_face_point = get_best_candidate_point_by_z(face_candidates, source_point)
            if best_face_point is not None:
                return best_face_point, max_xy_distance, "faceAtSourceXY"
            return face_candidates[0], max_xy_distance, "faceAtSourceXY"

    if precise_selection:
        projected_point, projected_distance = get_projected_point_on_face(
            face,
            source_point,
            transform,
        )
        if projected_point is not None:
            return projected_point, projected_distance, "faceProjectedFallback"

    if picked_point is not None and precise_selection:
        try:
            target_point = XYZ(source_point.X, source_point.Y, picked_point.Z)
            dx = picked_point.X - source_point.X
            dy = picked_point.Y - source_point.Y
            xy_distance = ((dx * dx) + (dy * dy)) ** 0.5
            return target_point, xy_distance, "pickedPointZFallback"
        except Exception:
            pass

    return None, None, None


def get_best_candidate_point_by_z(candidate_points, source_point):
    if not candidate_points:
        return None

    try:
        source_z = source_point.Z
    except Exception:
        source_z = None

    if source_z is None:
        return candidate_points[0]

    best_point = None
    best_gap = None
    for candidate_point in candidate_points:
        try:
            z_gap = abs(candidate_point.Z - source_z)
        except Exception:
            continue

        if best_gap is None or z_gap < best_gap:
            best_gap = z_gap
            best_point = candidate_point

    return best_point or candidate_points[0]


def build_face_triangle_cache(face, transform=None):
    triangles = []
    min_x = None
    max_x = None
    min_y = None
    max_y = None

    try:
        mesh = face.Triangulate()
    except Exception:
        mesh = None

    if mesh is None:
        return {
            "triangles": triangles,
            "minX": min_x,
            "maxX": max_x,
            "minY": min_y,
            "maxY": max_y,
        }

    try:
        triangle_count = mesh.NumTriangles
    except Exception:
        triangle_count = 0

    for triangle_index in range(triangle_count):
        try:
            triangle = mesh.get_Triangle(triangle_index)
            p1 = apply_transform_to_point(triangle.get_Vertex(0), transform)
            p2 = apply_transform_to_point(triangle.get_Vertex(1), transform)
            p3 = apply_transform_to_point(triangle.get_Vertex(2), transform)
        except Exception:
            continue

        if p1 is None or p2 is None or p3 is None:
            continue

        triangles.append((p1, p2, p3))
        for point in [p1, p2, p3]:
            if min_x is None or point.X < min_x:
                min_x = point.X
            if max_x is None or point.X > max_x:
                max_x = point.X
            if min_y is None or point.Y < min_y:
                min_y = point.Y
            if max_y is None or point.Y > max_y:
                max_y = point.Y

    return {
        "triangles": triangles,
        "minX": min_x,
        "maxX": max_x,
        "minY": min_y,
        "maxY": max_y,
    }


def is_point_near_triangle_cache_xy(source_point, triangle_cache, margin=0.0):
    if source_point is None or triangle_cache is None:
        return True

    try:
        min_x = triangle_cache.get("minX")
        max_x = triangle_cache.get("maxX")
        min_y = triangle_cache.get("minY")
        max_y = triangle_cache.get("maxY")
    except Exception:
        return True

    if min_x is None or max_x is None or min_y is None or max_y is None:
        return True

    try:
        px = source_point.X
        py = source_point.Y
    except Exception:
        return True

    if px < (min_x - margin) or px > (max_x + margin):
        return False
    if py < (min_y - margin) or py > (max_y + margin):
        return False

    return True


def get_local_point_from_face(
    face,
    source_point,
    transform=None,
    allow_edge_fallback=True,
    inside_tolerance=1e-6,
    triangle_cache=None,
):
    cache = triangle_cache
    if cache is None:
        cache = build_face_triangle_cache(face, transform)

    try:
        triangles = cache.get("triangles")
    except Exception:
        triangles = cache

    if not triangles:
        return None, None

    px = source_point.X
    py = source_point.Y
    source_z = None
    try:
        source_z = source_point.Z
    except Exception:
        source_z = None
    best_inside_point = None
    best_inside_gap = None
    best_edge_point = None
    best_edge_distance_sq = None

    for p1, p2, p3 in triangles:

        barycentric = get_triangle_barycentric_2d(px, py, p1, p2, p3)
        if barycentric is not None:
            w1, w2, w3 = barycentric
            tolerance = inside_tolerance
            if w1 >= -tolerance and w2 >= -tolerance and w3 >= -tolerance:
                z_value = (w1 * p1.Z) + (w2 * p2.Z) + (w3 * p3.Z)
                z_gap = None
                if source_z is not None:
                    try:
                        z_gap = abs(z_value - source_z)
                    except Exception:
                        z_gap = None

                if (
                    best_inside_point is None
                    or z_gap is None
                    or best_inside_gap is None
                    or z_gap < best_inside_gap
                ):
                    best_inside_gap = z_gap
                    best_inside_point = XYZ(px, py, z_value)

        segment_checks = [(p1, p2), (p2, p3), (p3, p1)]
        for start_point, end_point in segment_checks:
            qx, qy, t = get_closest_point_on_segment_2d(
                px,
                py,
                start_point.X,
                start_point.Y,
                end_point.X,
                end_point.Y,
            )
            qz = start_point.Z + ((end_point.Z - start_point.Z) * t)
            dx = qx - px
            dy = qy - py
            distance_sq = (dx * dx) + (dy * dy)
            if best_edge_distance_sq is None or distance_sq < best_edge_distance_sq:
                best_edge_distance_sq = distance_sq
                best_edge_point = XYZ(qx, qy, qz)

    if best_inside_point is not None:
        return best_inside_point, 0.0

    if not allow_edge_fallback:
        return None, None

    if best_edge_point is None or best_edge_distance_sq is None:
        return None, None

    return best_edge_point, best_edge_distance_sq ** 0.5


def get_face_hit_data(face, picked_point, transform=None):
    picked_inside_tolerance = 0.0
    try:
        picked_inside_tolerance = to_internal_mm(face_pick_tolerance_mm) / 10.0
    except Exception:
        picked_inside_tolerance = 0.01

    inside_point, _ = get_local_point_from_face(
        face,
        picked_point,
        transform,
        allow_edge_fallback=False,
        inside_tolerance=picked_inside_tolerance,
    )
    if inside_point is not None:
        try:
            dx = inside_point.X - picked_point.X
            dy = inside_point.Y - picked_point.Y
            dz = inside_point.Z - picked_point.Z
            distance_3d = ((dx * dx) + (dy * dy) + (dz * dz)) ** 0.5
        except Exception:
            distance_3d = None
        return {
            "point": inside_point,
            "distance3d": distance_3d,
            "rank": 0,
            "mode": "inside",
        }

    edge_point, _ = get_local_point_from_face(
        face,
        picked_point,
        transform,
        allow_edge_fallback=True,
    )
    if edge_point is not None:
        try:
            dx = edge_point.X - picked_point.X
            dy = edge_point.Y - picked_point.Y
            dz = edge_point.Z - picked_point.Z
            distance_3d = ((dx * dx) + (dy * dy) + (dz * dz)) ** 0.5
        except Exception:
            distance_3d = None
        return {
            "point": edge_point,
            "distance3d": distance_3d,
            "rank": 1,
            "mode": "edge",
        }

    projected_point, projected_distance = get_projected_point_on_face(
        face,
        picked_point,
        transform,
    )
    if projected_point is not None:
        return {
            "point": projected_point,
            "distance3d": projected_distance,
            "rank": 2,
            "mode": "projected",
        }

    return None


def get_planar_face_point_at_same_xy(face, source_point, transform=None):
    if not isinstance(face, PlanarFace):
        return None, None

    local_origin = None
    local_normal = None
    try:
        local_origin = face.Origin
        local_normal = face.FaceNormal
    except Exception:
        return None, None

    world_origin = apply_transform_to_point(local_origin, transform)
    world_normal = local_normal
    if transform is not None:
        try:
            world_normal = transform.OfVector(local_normal)
        except Exception:
            world_normal = local_normal

    try:
        world_normal = world_normal.Normalize()
    except Exception:
        pass

    try:
        nz = world_normal.Z
        if abs(nz) <= 1e-9:
            return None, None
        z_value = world_origin.Z - (
            ((source_point.X - world_origin.X) * world_normal.X)
            + ((source_point.Y - world_origin.Y) * world_normal.Y)
        ) / nz
        return XYZ(source_point.X, source_point.Y, z_value), 0.0
    except Exception:
        return None, None


def get_world_plane_from_planar_face(face, transform=None):
    if not isinstance(face, PlanarFace):
        return None, None

    try:
        local_origin = face.Origin
        local_normal = face.FaceNormal
    except Exception:
        return None, None

    world_origin = apply_transform_to_point(local_origin, transform)
    world_normal = local_normal
    if transform is not None:
        try:
            world_normal = transform.OfVector(local_normal)
        except Exception:
            world_normal = local_normal

    if world_origin is None or world_normal is None:
        return None, None

    try:
        world_normal = world_normal.Normalize()
    except Exception:
        pass

    return world_origin, world_normal


def get_planar_face_picked_z_gap(face, picked_point, transform=None):
    if not isinstance(face, PlanarFace) or picked_point is None:
        return None

    planar_point, _ = get_planar_face_point_at_same_xy(
        face,
        picked_point,
        transform,
    )
    if planar_point is None:
        return None

    try:
        return abs(planar_point.Z - picked_point.Z)
    except Exception:
        return None


def is_planar_face_close_to_picked_point(face, picked_point, transform=None, tolerance_mm=None):
    if tolerance_mm is None:
        tolerance_mm = planar_pick_z_tolerance_mm

    if not isinstance(face, PlanarFace) or picked_point is None:
        return True

    z_gap = get_planar_face_picked_z_gap(
        face,
        picked_point,
        transform,
    )
    if z_gap is None:
        return False

    return z_gap <= to_internal_mm(tolerance_mm)


def orient_triangle_points(points, reference_normal):
    if points is None or len(points) != 3 or reference_normal is None:
        return points

    try:
        v1 = points[1] - points[0]
        v2 = points[2] - points[0]
        triangle_normal = v1.CrossProduct(v2)
        if triangle_normal.DotProduct(reference_normal) < 0:
            return [points[0], points[2], points[1]]
    except Exception:
        pass

    return points


def get_plane_basis_from_normal(normal):
    if normal is None:
        return None, None

    try:
        normal = normal.Normalize()
    except Exception:
        pass

    reference = XYZ.BasisZ
    try:
        if abs(normal.DotProduct(reference)) > 0.95:
            reference = XYZ.BasisX
    except Exception:
        reference = XYZ.BasisX

    try:
        axis_x = normal.CrossProduct(reference).Normalize()
        axis_y = normal.CrossProduct(axis_x).Normalize()
        return axis_x, axis_y
    except Exception:
        return None, None


def make_xyz(x_value, y_value, z_value):
    try:
        return XYZ(x_value, y_value, z_value)
    except Exception:
        return None


def add_vectors(vector_a, vector_b):
    if vector_a is None or vector_b is None:
        return None

    return make_xyz(
        vector_a.X + vector_b.X,
        vector_a.Y + vector_b.Y,
        vector_a.Z + vector_b.Z,
    )


def scale_vector(vector, scalar):
    if vector is None:
        return None

    try:
        return vector.Multiply(scalar)
    except Exception:
        return make_xyz(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar,
        )


def add_vector_to_point(point, vector):
    if point is None or vector is None:
        return None

    return make_xyz(
        point.X + vector.X,
        point.Y + vector.Y,
        point.Z + vector.Z,
    )


def get_vector_between_points(start_point, end_point):
    if start_point is None or end_point is None:
        return None

    try:
        return XYZ(
            end_point.X - start_point.X,
            end_point.Y - start_point.Y,
            end_point.Z - start_point.Z,
        )
    except Exception:
        return None


def get_dot_product(vector_a, vector_b):
    if vector_a is None or vector_b is None:
        return None

    try:
        return (
            (vector_a.X * vector_b.X)
            + (vector_a.Y * vector_b.Y)
            + (vector_a.Z * vector_b.Z)
        )
    except Exception:
        return None


def trace_planar_face_to_model_curves(face, transform=None):
    if not isinstance(face, PlanarFace):
        return [], None

    plane_origin, plane_normal = get_world_plane_from_planar_face(face, transform)
    if plane_origin is None or plane_normal is None:
        return [], None

    try:
        sketch_plane = SketchPlane.Create(
            doc,
            Plane.CreateByNormalAndOrigin(plane_normal, plane_origin),
        )
    except Exception:
        return [], None

    created_ids = []

    try:
        edge_loops = face.EdgeLoops
    except Exception:
        edge_loops = []

    for edge_loop in edge_loops:
        for edge in edge_loop:
            try:
                curve = edge.AsCurve()
            except Exception:
                curve = None

            if curve is None:
                continue

            if transform is not None:
                try:
                    curve = curve.CreateTransformed(transform)
                except Exception:
                    pass

            try:
                model_curve = doc.Create.NewModelCurve(curve, sketch_plane)
            except Exception:
                model_curve = None

            if model_curve is not None:
                try:
                    comment_parameter = model_curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                    if comment_parameter is not None and (not comment_parameter.IsReadOnly):
                        comment_parameter.Set(face_highlight_curve_marker)
                except Exception:
                    pass
                created_ids.append(safe_int_id(model_curve))

    return created_ids, safe_int_id(sketch_plane)


def build_curve_loops_from_planar_face(face, transform=None):
    if not isinstance(face, PlanarFace):
        return []

    curve_loops = []

    try:
        edge_loops = face.EdgeLoops
    except Exception:
        edge_loops = []

    for edge_loop in edge_loops:
        curve_loop = CurveLoop()
        appended_count = 0

        for edge in edge_loop:
            try:
                curve = edge.AsCurve()
            except Exception:
                curve = None

            if curve is None:
                continue

            if transform is not None:
                try:
                    curve = curve.CreateTransformed(transform)
                except Exception:
                    pass

            try:
                curve_loop.Append(curve)
                appended_count += 1
            except Exception:
                pass

        if appended_count > 1:
            curve_loops.append(curve_loop)

    return curve_loops


def create_helper_directshape_from_planar_face(
    face,
    transform=None,
    source_points=None,
    picked_point=None,
):
    if not isinstance(face, PlanarFace):
        return None, [], "The selected face is not a PlanarFace."

    plane_origin, plane_normal = get_world_plane_from_planar_face(face, transform)
    if plane_origin is None or plane_normal is None:
        return None, [], "The world plane could not be resolved from the planar face."

    axis_x, axis_y = get_plane_basis_from_normal(plane_normal)
    if axis_x is None or axis_y is None:
        return None, [], "The plane basis could not be resolved."

    helper_points = []
    if source_points:
        helper_points.extend([point for point in source_points if point is not None])
    if picked_point is not None:
        helper_points.append(picked_point)
    if not helper_points:
        helper_points.append(plane_origin)

    margin = to_internal_mm(helper_face_margin_mm)
    min_x = None
    max_x = None
    min_y = None
    max_y = None

    for point in helper_points:
        planar_point, _ = get_planar_face_point_at_same_xy(face, point, transform)
        world_point = planar_point if planar_point is not None else point
        vector = get_vector_between_points(plane_origin, world_point)
        x_value = get_dot_product(vector, axis_x)
        y_value = get_dot_product(vector, axis_y)
        if x_value is None or y_value is None:
            continue

        if min_x is None or x_value < min_x:
            min_x = x_value
        if max_x is None or x_value > max_x:
            max_x = x_value
        if min_y is None or y_value < min_y:
            min_y = y_value
        if max_y is None or y_value > max_y:
            max_y = y_value

    if min_x is None or min_y is None or max_x is None or max_y is None:
        return None, [], "The helper rectangle extents could not be calculated."

    if abs(max_x - min_x) < margin:
        min_x -= margin * 0.5
        max_x += margin * 0.5
    if abs(max_y - min_y) < margin:
        min_y -= margin * 0.5
        max_y += margin * 0.5

    min_x -= margin
    max_x += margin
    min_y -= margin
    max_y += margin

    vx_min = scale_vector(axis_x, min_x)
    vx_max = scale_vector(axis_x, max_x)
    vy_min = scale_vector(axis_y, min_y)
    vy_max = scale_vector(axis_y, max_y)

    p1 = add_vector_to_point(plane_origin, add_vectors(vx_min, vy_min))
    p2 = add_vector_to_point(plane_origin, add_vectors(vx_max, vy_min))
    p3 = add_vector_to_point(plane_origin, add_vectors(vx_max, vy_max))
    p4 = add_vector_to_point(plane_origin, add_vectors(vx_min, vy_max))

    curve_loop = CurveLoop()
    try:
        curve_loop.Append(Line.CreateBound(p1, p2))
        curve_loop.Append(Line.CreateBound(p2, p3))
        curve_loop.Append(Line.CreateBound(p3, p4))
        curve_loop.Append(Line.CreateBound(p4, p1))
    except Exception as loop_error:
        return None, [], "The helper rectangle could not be built: {0}".format(loop_error)

    curve_loops = [curve_loop]

    try:
        extrusion_dir = plane_normal.Normalize()
    except Exception:
        extrusion_dir = plane_normal

    extrusion_depth = to_internal_mm(helper_face_thickness_mm)
    if extrusion_depth <= 0:
        extrusion_depth = 1.0 / 304.8

    try:
        helper_solid = GeometryCreationUtilities.CreateExtrusionGeometry(
            curve_loops,
            extrusion_dir,
            extrusion_depth,
        )
    except Exception as extrusion_error:
        helper_solid = None
        helper_error = "CreateExtrusionGeometry failed: {0}".format(extrusion_error)
    else:
        helper_error = None

    try:
        direct_shape = DirectShape.CreateElement(
            doc,
            doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel).Id,
        )
    except Exception as create_error:
        direct_shape = None
        helper_error = "DirectShape.CreateElement failed: {0}".format(create_error)
    else:
        helper_error = None

    if direct_shape is None:
        return None, [], helper_error or "DirectShape.CreateElement returned null."

    try:
        direct_shape.ApplicationId = "Codex"
    except Exception:
        pass

    try:
        direct_shape.ApplicationDataId = "linked_planar_face_helper"
    except Exception:
        pass

    if helper_solid is not None:
        try:
            geometry_objects = List[GeometryObject]()
            geometry_objects.Add(helper_solid)
            direct_shape.SetShape(geometry_objects)
            return direct_shape, curve_loops, None
        except Exception as setshape_error:
            helper_error = "DirectShape.SetShape failed: {0}".format(setshape_error)

    return None, [], helper_error or "CreateExtrusionGeometry and DirectShape.SetShape both failed."


def create_rectangle_curve_loop(center_point, size_x, size_y):
    if center_point is None:
        return None

    half_x = size_x * 0.5
    half_y = size_y * 0.5

    p1 = XYZ(center_point.X - half_x, center_point.Y - half_y, center_point.Z)
    p2 = XYZ(center_point.X + half_x, center_point.Y - half_y, center_point.Z)
    p3 = XYZ(center_point.X + half_x, center_point.Y + half_y, center_point.Z)
    p4 = XYZ(center_point.X - half_x, center_point.Y + half_y, center_point.Z)

    curve_loop = CurveLoop()
    curve_loop.Append(Line.CreateBound(p1, p2))
    curve_loop.Append(Line.CreateBound(p2, p3))
    curve_loop.Append(Line.CreateBound(p3, p4))
    curve_loop.Append(Line.CreateBound(p4, p1))
    return curve_loop


def create_object_markers_for_points(points, size_mm=None, height_mm=None):
    marker_ids = []

    marker_size = to_internal_mm(size_mm if size_mm is not None else object_marker_size_mm)
    marker_height = to_internal_mm(height_mm if height_mm is not None else object_marker_height_mm)
    if marker_size <= 0 or marker_height <= 0:
        return marker_ids, "Object marker size or height must be greater than zero."

    for point in points or []:
        if point is None:
            continue

        try:
            sketch_plane = SketchPlane.Create(
                doc,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, point),
            )
            half_size = marker_size * 0.5
            line_a = Line.CreateBound(
                XYZ(point.X - half_size, point.Y, point.Z),
                XYZ(point.X + half_size, point.Y, point.Z),
            )
            line_b = Line.CreateBound(
                XYZ(point.X, point.Y - half_size, point.Z),
                XYZ(point.X, point.Y + half_size, point.Z),
            )

            for marker_curve in [line_a, line_b]:
                model_curve = doc.Create.NewModelCurve(marker_curve, sketch_plane)
                try:
                    comment_parameter = model_curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                    if comment_parameter is not None and (not comment_parameter.IsReadOnly):
                        comment_parameter.Set(object_highlight_curve_marker)
                except Exception:
                    pass
                marker_ids.append(safe_int_id(model_curve))
        except Exception:
            pass

    return marker_ids, None


def delete_codex_helper_shapes():
    deleted_ids = []
    helper_keys = set(
        [
            "linked_planar_face_helper",
            "selected_object_marker",
        ]
    )

    try:
        direct_shapes = FilteredElementCollector(doc).OfClass(DirectShape)
    except Exception:
        direct_shapes = []

    for direct_shape in direct_shapes:
        try:
            if str(direct_shape.ApplicationId) != "Codex":
                continue
        except Exception:
            continue

        try:
            data_id = str(direct_shape.ApplicationDataId)
        except Exception:
            data_id = ""

        if data_id not in helper_keys:
            continue

        try:
            deleted_ids.append(safe_int_id(direct_shape))
            doc.Delete(direct_shape.Id)
        except Exception:
            pass

    curve_markers = set(
        [
            face_highlight_curve_marker,
            object_highlight_curve_marker,
        ]
    )

    try:
        curve_elements = FilteredElementCollector(doc).OfClass(CurveElement)
    except Exception:
        curve_elements = []

    for curve_element in curve_elements:
        try:
            comment_parameter = curve_element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
            comment_value = comment_parameter.AsString() if comment_parameter is not None else None
        except Exception:
            comment_value = None

        if comment_value not in curve_markers:
            continue

        try:
            deleted_ids.append(safe_int_id(curve_element))
            doc.Delete(curve_element.Id)
        except Exception:
            pass

    return [item for item in deleted_ids if item is not None]


def get_import_geometry_options():
    return get_element_geometry_options()


def iter_faces_from_geometry(geometry_element, current_transform=None):
    if geometry_element is None:
        return

    for geometry_object in geometry_element:
        if isinstance(geometry_object, GeometryInstance):
            geometry_transform = current_transform
            try:
                instance_transform = geometry_object.Transform
            except Exception:
                instance_transform = None

            geometry_transform = multiply_transforms(
                geometry_transform,
                instance_transform,
            )

            try:
                instance_geometry = geometry_object.GetInstanceGeometry()
            except Exception:
                instance_geometry = None

            if instance_geometry is not None:
                for nested_face, nested_transform in iter_faces_from_geometry(
                    instance_geometry,
                    geometry_transform,
                ):
                    yield nested_face, nested_transform

            try:
                symbol_geometry = geometry_object.GetSymbolGeometry()
            except Exception:
                symbol_geometry = None

            if symbol_geometry is not None:
                for nested_face, nested_transform in iter_faces_from_geometry(
                    symbol_geometry,
                    geometry_transform,
                ):
                    yield nested_face, nested_transform
            continue

        if not isinstance(geometry_object, Solid):
            continue

        try:
            faces = geometry_object.Faces
        except Exception:
            faces = []

        for face in faces:
            if isinstance(face, Face):
                yield face, current_transform


def find_best_face_from_geometry(geometry_element, global_point, base_transform=None):
    best_face = None
    best_transform = None
    best_hit_mode = None
    best_score = None

    for face, face_transform in iter_faces_from_geometry(geometry_element, base_transform):
        if not is_upward_facing_face(face, face_transform):
            continue

        if not is_planar_face_close_to_picked_point(
            face,
            global_point,
            face_transform,
        ):
            continue

        hit_data = get_face_hit_data(
            face,
            global_point,
            face_transform,
        )
        if hit_data is None:
            continue

        distance_3d = hit_data.get("distance3d")
        if distance_3d is None:
            continue

        score = (hit_data.get("rank", 99), distance_3d)
        if best_score is None or score < best_score:
            best_score = score
            best_face = face
            best_transform = face_transform
            best_hit_mode = hit_data.get("mode")

    return best_face, best_transform, best_hit_mode


def get_candidate_point_for_source_on_face(face, source_point, transform=None, triangle_cache=None):
    target_point, xy_distance = get_local_point_from_face(
        face,
        source_point,
        transform,
        allow_edge_fallback=False,
        triangle_cache=triangle_cache,
    )
    if target_point is not None:
        return target_point, xy_distance, "faceAtSourceXY"

    if isinstance(face, PlanarFace):
        planar_point, planar_distance = get_planar_face_point_at_same_xy(
            face,
            source_point,
            transform,
        )
        if planar_point is not None:
            return planar_point, planar_distance, "planarFaceSameXY"

    return None, None, None


def find_best_face_for_sources(
    geometry_element,
    source_points,
    base_transform=None,
    picked_point=None,
):
    best_face = None
    best_transform = None
    best_mode = None
    best_score = None

    if geometry_element is None or not source_points:
        return None, None, None

    for face, face_transform in iter_faces_from_geometry(geometry_element, base_transform):
        if not is_upward_facing_face(face, face_transform):
            continue

        triangle_cache = build_face_triangle_cache(face, face_transform)
        pick_rank = 99
        pick_distance = None
        if picked_point is not None:
            hit_data = get_face_hit_data(
                face,
                picked_point,
                face_transform,
            )
            if hit_data is None:
                continue

            if not is_planar_face_close_to_picked_point(
                face,
                picked_point,
                face_transform,
            ):
                continue

            pick_rank = hit_data.get("rank", 99)
            pick_distance = hit_data.get("distance3d")
            if pick_distance is None:
                continue

        matched_count = 0
        vertical_sum = 0.0
        xy_sum = 0.0
        face_mode = None

        for source_point in source_points:
            if source_point is None:
                continue

            candidate_point, xy_distance, candidate_mode = get_candidate_point_for_source_on_face(
                face,
                source_point,
                face_transform,
                triangle_cache,
            )
            if candidate_point is None:
                continue

            matched_count += 1
            face_mode = candidate_mode
            try:
                vertical_sum += abs(candidate_point.Z - source_point.Z)
            except Exception:
                pass
            try:
                if xy_distance is not None:
                    xy_sum += xy_distance
            except Exception:
                pass

        if matched_count <= 0:
            continue

        if pick_distance is None:
            pick_distance = 0.0

        score = (pick_rank, pick_distance, -matched_count, vertical_sum, xy_sum)
        if best_score is None or score < best_score:
            best_score = score
            best_face = face
            best_transform = face_transform
            best_mode = face_mode

    return best_face, best_transform, best_mode


def find_face_in_import_from_reference(element, reference, source_points=None):
    if not isinstance(element, ImportInstance):
        return None, None, None

    import_transform = None
    try:
        import_transform = element.GetTotalTransform()
    except Exception:
        try:
            import_transform = element.GetTransform()
        except Exception:
            import_transform = None

    geometry_face = None
    try:
        geometry_face = element.GetGeometryObjectFromReference(reference)
    except Exception:
        geometry_face = None

    if isinstance(geometry_face, Face):
        return geometry_face, import_transform, "directReference"

    global_point = get_reference_point_on_face(
        geometry_face if isinstance(geometry_face, Face) else None,
        reference,
        import_transform,
    )

    geometry_element = None
    try:
        geometry_element = element.get_Geometry(get_import_geometry_options())
    except Exception:
        geometry_element = None

    if geometry_element is None:
        return None, None, None

    if source_points:
        best_face, best_transform, best_hit_mode = find_best_face_for_sources(
            geometry_element,
            source_points,
            import_transform,
            global_point,
        )
        if best_face is not None:
            return best_face, best_transform, best_hit_mode

    if global_point is None:
        return None, None, None

    best_face, best_transform, best_hit_mode = find_best_face_from_geometry(
        geometry_element,
        global_point,
        import_transform,
    )
    return best_face, best_transform, best_hit_mode


def find_best_face_in_element_for_sources(element, source_points, picked_point=None):
    if element is None:
        return None, None, None

    geometry_element = None
    try:
        geometry_element = element.get_Geometry(get_import_geometry_options())
    except Exception:
        geometry_element = None

    if geometry_element is None:
        return None, None, None

    return find_best_face_for_sources(
        geometry_element,
        source_points,
        None,
        picked_point,
    )


def find_face_in_link_from_reference(
    link_instance,
    reference,
    prefer_geometry_search=False,
    source_points=None,
):
    if not isinstance(link_instance, RevitLinkInstance):
        return None, None, None, None

    global_point = None
    try:
        global_point = reference.GlobalPoint
    except Exception:
        global_point = None

    linked_element_id = None
    try:
        linked_element_id = reference.LinkedElementId
    except Exception:
        linked_element_id = None

    link_doc = None
    try:
        link_doc = link_instance.GetLinkDocument()
    except Exception:
        link_doc = None

    if link_doc is None or linked_element_id is None:
        return None, None, None, None

    try:
        linked_element = link_doc.GetElement(linked_element_id)
    except Exception:
        linked_element = None

    if linked_element is None:
        return None, None, None, None

    try:
        link_transform = link_instance.GetTotalTransform()
    except Exception:
        try:
            link_transform = link_instance.GetTransform()
        except Exception:
            link_transform = None

    try:
        linked_geometry = linked_element.get_Geometry(get_import_geometry_options())
    except Exception:
        linked_geometry = None

    best_linked_element = linked_element
    best_face = None
    best_transform = None
    best_hit_mode = None
    direct_face = None
    direct_transform = None
    direct_hit_mode = None

    link_reference = None
    if not prefer_geometry_search:
        try:
            link_reference = reference.CreateReferenceInLink()
        except Exception:
            link_reference = None

        if link_reference is not None:
            try:
                geometry_object = linked_element.GetGeometryObjectFromReference(link_reference)
            except Exception:
                geometry_object = None

            if isinstance(geometry_object, Face) and is_planar_face_close_to_picked_point(
                geometry_object,
                global_point,
                link_transform,
            ):
                direct_face = geometry_object
                direct_transform = link_transform
                direct_hit_mode = "direct"

        if direct_face is None:
            try:
                geometry_object = linked_element.GetGeometryObjectFromReference(reference)
            except Exception:
                geometry_object = None

            if isinstance(geometry_object, Face) and is_planar_face_close_to_picked_point(
                geometry_object,
                global_point,
                link_transform,
            ):
                direct_face = geometry_object
                direct_transform = link_transform
                direct_hit_mode = "direct"

    if source_points:
        best_face, best_transform, best_hit_mode = find_best_face_for_sources(
            linked_geometry,
            source_points,
            link_transform,
            global_point,
        )
        if best_face is not None:
            return best_linked_element, best_face, best_transform, "familyAuto:" + str(best_hit_mode)

    if global_point is not None:
        best_face, best_transform, best_hit_mode = find_best_face_from_geometry(
            linked_geometry,
            global_point,
            link_transform,
        )

    if prefer_geometry_search and best_face is not None:
        return best_linked_element, best_face, best_transform, "auto:" + str(best_hit_mode)

    if direct_face is not None:
        return best_linked_element, direct_face, direct_transform, direct_hit_mode

    if best_face is not None:
        return best_linked_element, best_face, best_transform, best_hit_mode

    return best_linked_element, None, None, None


def pick_face_references():
    if uidoc is None:
        return [], "ActiveUIDocument is not available.", None

    if multi_face_pick_enabled:
        try:
            references = uidoc.Selection.PickObjects(
                ObjectType.PointOnElement,
                "Select one or more Revit/DWG/Revit Link faces for Z-only attachment",
            )
            return list(references or []), None, "pointOnElementMulti"
        except Exception:
            pass

    try:
        reference = uidoc.Selection.PickObject(
            ObjectType.PointOnElement,
            "Select one Revit face, DWG face, or Revit Link face for Z-only attachment",
        )
        return [reference], None, "pointOnElement"
    except Exception:
        pass

    try:
        reference = uidoc.Selection.PickObject(
            ObjectType.Face,
            "Select one Revit face for Z-only attachment",
        )
        return [reference], None, "face"
    except Exception:
        pass

    try:
        reference = uidoc.Selection.PickObject(
            ObjectType.LinkedElement,
            "Select one Revit Link element for Z-only attachment if direct face picking is not available",
        )
        return [reference], None, "linkedElement"
    except Exception:
        pass

    try:
        reference = uidoc.Selection.PickObject(
            ObjectType.Element,
            "Select one host element for Z-only attachment",
        )
        return [reference], None, "element"
    except Exception as pick_error:
        return [], str(pick_error), None


def build_face_record(reference, pick_kind=None, source_points=None):
    reference_source_points = source_points
    if pick_kind == "pointOnElementMulti":
        reference_source_points = None

    (
        host_element,
        selected_face,
        selected_face_transform,
        linked_element,
        picked_point,
        selection_mode,
    ) = get_face_from_reference(
        reference,
        pick_kind,
        reference_source_points,
    )

    return {
        "reference": reference,
        "hostElement": host_element,
        "face": selected_face,
        "transform": selected_face_transform,
        "linkedElement": linked_element,
        "pickedPoint": picked_point,
        "selectionMode": selection_mode,
        "triangleCache": build_face_triangle_cache(selected_face, selected_face_transform)
        if selected_face is not None
        else [],
    }


def summarize_face_record(face_record, index=None):
    host_element = face_record.get("hostElement")
    linked_element = face_record.get("linkedElement")
    selected_face = face_record.get("face")
    selection_mode = face_record.get("selectionMode")

    return {
        "selectionIndex": index,
        "hostId": safe_int_id(host_element),
        "hostType": type(host_element).__name__ if host_element is not None else None,
        "linkedElementId": safe_int_id(linked_element),
        "linkedElementType": type(linked_element).__name__ if linked_element is not None else None,
        "pickedPoint": xyz_to_list(face_record.get("pickedPoint")),
        "selectionMode": selection_mode,
        "isPlanarFace": isinstance(selected_face, PlanarFace),
        "useFastMode": use_fast_mode,
    }


def get_target_mode_rank(target_mode):
    mode_ranks = {
        "faceAtSourceXY": 0,
        "faceEdgeFallbackXY": 1,
        "multiFaceGlobalFallbackXY": 2,
        "planarFaceSameXY": 3,
        "horizontalFacePlaneZ": 4,
        "faceProjectedFallback": 5,
        "pickedPointZAbsolute": 6,
        "pickedPointZFallback": 7,
    }
    return mode_ranks.get(target_mode, 99)


def get_best_face_target_for_family(face_records, source_point, source_bottom_points=None):
    best_record = None
    best_target_point = None
    best_xy_distance = None
    best_target_mode = None
    best_score = None
    bbox_margin = to_internal_mm(import_edge_fallback_tolerance_mm)

    for index, face_record in enumerate(face_records or []):
        selected_face = face_record.get("face")
        if selected_face is None:
            continue

        triangle_cache = face_record.get("triangleCache")
        if not is_point_near_triangle_cache_xy(source_point, triangle_cache, bbox_margin):
            continue

        target_point, xy_distance, target_mode = get_preferred_target_point(
            selected_face,
            source_point,
            source_bottom_points,
            face_record.get("pickedPoint"),
            face_record.get("transform"),
            face_record.get("selectionMode"),
            triangle_cache,
        )
        if target_point is None:
            continue

        xy_distance_value = xy_distance
        if xy_distance_value is None:
            xy_distance_value = 1e9

        try:
            vertical_gap = abs(target_point.Z - source_point.Z)
        except Exception:
            vertical_gap = 1e9

        score = (
            get_target_mode_rank(target_mode),
            xy_distance_value,
            vertical_gap,
            index,
        )
        if best_score is None or score < best_score:
            best_score = score
            best_record = face_record
            best_target_point = target_point
            best_xy_distance = xy_distance
            best_target_mode = target_mode

    if best_record is not None:
        return best_record, best_target_point, best_xy_distance, best_target_mode

    fallback_tolerance = to_internal_mm(multi_face_global_fallback_tolerance_mm)
    fallback_best_record = None
    fallback_best_point = None
    fallback_best_distance = None
    fallback_best_gap = None

    for face_record in face_records or []:
        selected_face = face_record.get("face")
        if selected_face is None:
            continue

        triangle_cache = face_record.get("triangleCache")
        if not is_point_near_triangle_cache_xy(source_point, triangle_cache, fallback_tolerance):
            continue

        target_point, xy_distance = get_local_point_from_face(
            selected_face,
            source_point,
            face_record.get("transform"),
            allow_edge_fallback=True,
            triangle_cache=triangle_cache,
        )
        if target_point is None or xy_distance is None:
            continue
        if xy_distance > fallback_tolerance:
            continue

        try:
            vertical_gap = abs(target_point.Z - source_point.Z)
        except Exception:
            vertical_gap = None

        if fallback_best_record is None:
            fallback_best_record = face_record
            fallback_best_point = target_point
            fallback_best_distance = xy_distance
            fallback_best_gap = vertical_gap
            continue

        if xy_distance < fallback_best_distance:
            fallback_best_record = face_record
            fallback_best_point = target_point
            fallback_best_distance = xy_distance
            fallback_best_gap = vertical_gap
            continue

        if (
            abs(xy_distance - fallback_best_distance) <= 1e-9
            and vertical_gap is not None
            and fallback_best_gap is not None
            and vertical_gap < fallback_best_gap
        ):
            fallback_best_record = face_record
            fallback_best_point = target_point
            fallback_best_distance = xy_distance
            fallback_best_gap = vertical_gap

    if fallback_best_record is not None:
        return (
            fallback_best_record,
            fallback_best_point,
            fallback_best_distance,
            "multiFaceGlobalFallbackXY",
        )

    return best_record, best_target_point, best_xy_distance, best_target_mode


def get_face_from_reference(reference, pick_kind=None, source_points=None):
    if reference is None:
        return None, None, None, None, None, None

    try:
        element = doc.GetElement(reference.ElementId)
    except Exception:
        element = None

    if element is None:
        return None, None, None, None, None, None

    global_point = None
    try:
        global_point = reference.GlobalPoint
    except Exception:
        global_point = None

    try:
        geometry_object = element.GetGeometryObjectFromReference(reference)
    except Exception:
        geometry_object = None

    if isinstance(element, ImportInstance):
        import_face, import_transform, import_hit_mode = find_face_in_import_from_reference(
            element,
            reference,
            source_points,
        )
        if import_face is not None:
            import_point = get_reference_point_on_face(
                import_face,
                reference,
                import_transform,
            )
            return (
                element,
                import_face,
                import_transform,
                None,
                import_point,
                "import:" + str(import_hit_mode),
            )

    if isinstance(element, RevitLinkInstance):
        linked_element, linked_face, linked_transform, linked_hit_mode = find_face_in_link_from_reference(
            element,
            reference,
            prefer_geometry_search=(pick_kind == "linkedElement"),
            source_points=source_points if pick_kind == "linkedElement" else None,
        )
        if (
            linked_face is not None
            and linked_hit_mode == "edge"
            and pick_kind == "pointOnElement"
        ):
            auto_linked_element, auto_linked_face, auto_linked_transform, auto_linked_hit_mode = find_face_in_link_from_reference(
                element,
                reference,
                prefer_geometry_search=True,
                source_points=source_points,
            )
            if auto_linked_face is not None and auto_linked_hit_mode is not None:
                linked_element = auto_linked_element
                linked_face = auto_linked_face
                linked_transform = auto_linked_transform
                linked_hit_mode = auto_linked_hit_mode
        if linked_face is not None:
            return (
                element,
                linked_face,
                linked_transform,
                linked_element,
                global_point,
                "link:" + str(linked_hit_mode),
            )
        if global_point is not None:
            return element, None, None, linked_element, global_point, "link:pickedPointOnly"

    if isinstance(geometry_object, Face):
        direct_point = get_reference_point_on_face(
            geometry_object,
            reference,
            None,
        )
        return element, geometry_object, None, None, direct_point, "directFaceReference"

    current_face, current_transform, current_hit_mode = find_best_face_in_element_for_sources(
        element,
        source_points or [],
        global_point,
    )
    if current_face is not None:
        current_point = get_reference_point_on_face(
            current_face,
            reference,
            current_transform,
        )
        return (
            element,
            current_face,
            current_transform,
            None,
            current_point or global_point,
            "current:" + str(current_hit_mode),
        )

    linked_element, linked_face, linked_transform, linked_hit_mode = find_face_in_link_from_reference(
        element,
        reference,
        prefer_geometry_search=False,
        source_points=None,
    )
    if linked_face is not None:
        return (
            element,
            linked_face,
            linked_transform,
            linked_element,
            global_point,
            "link:" + str(linked_hit_mode),
        )

    fallback_face, fallback_transform, fallback_hit_mode = find_face_in_import_from_reference(
        element,
        reference,
    )
    if fallback_face is not None:
        fallback_point = get_reference_point_on_face(
            fallback_face,
            reference,
            fallback_transform,
        )
        return (
            element,
            fallback_face,
            fallback_transform,
            None,
            fallback_point or global_point,
            "import:" + str(fallback_hit_mode),
        )

    return element, None, None, None, global_point, None


def make_result(
    selected_family_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message="",
    selected_face_summary=None,
    selected_face_summaries=None,
    unhandled_error=None,
    traced_model_curve_ids=None,
    traced_sketch_plane_id=None,
    helper_directshape_id=None,
    helper_directshape_ids=None,
    object_marker_ids=None,
    deleted_helper_ids=None,
):
    return {
        "selectedFamilyCount": selected_family_count,
        "movedIds": moved_ids or [],
        "moveDetails": move_details or [],
        "skipped": skipped or [],
        "selectedFaceSummary": selected_face_summary or {},
        "selectedFaceSummaries": selected_face_summaries or [],
        "tracedModelCurveIds": traced_model_curve_ids or [],
        "tracedSketchPlaneId": traced_sketch_plane_id,
        "helperDirectShapeId": helper_directshape_id,
        "helperDirectShapeIds": helper_directshape_ids or [],
        "objectMarkerIds": object_marker_ids or [],
        "deletedHelperIds": deleted_helper_ids or [],
        "message": message,
        "unhandledError": unhandled_error,
    }


def main():
    min_vertical_gap_internal = to_internal_mm(min_vertical_gap_mm)
    max_vertical_move_internal = to_internal_mm(max_vertical_move_mm)

    families = get_selected_movable_elements()
    if not families:
        return make_result(
            selected_family_count=0,
            message="Select one or more family instances or groups first, then run the graph and pick a host face.",
        )

    family_source_points = []
    for family in families:
        family_source_points.append(get_instance_point(family))

    preselect_highlight_updated = set_ui_selection(families)
    preselect_refreshed = refresh_ui_view()
    preselect_shown = False

    face_references, pick_error, pick_kind = pick_face_references()
    if not face_references:
        return make_result(
            selected_family_count=len(families),
            message="Face selection was cancelled or failed.",
            selected_face_summary={
                "preselectHighlightUpdated": preselect_highlight_updated,
                "preselectRefreshed": preselect_refreshed,
                "preselectShown": preselect_shown,
            },
            unhandled_error=pick_error,
        )

    all_face_records = []
    selected_face_summaries = []
    usable_face_records = []
    for reference in face_references:
        face_record = build_face_record(reference, pick_kind, family_source_points)
        all_face_records.append(face_record)
        selected_face_summaries.append(
            summarize_face_record(face_record, len(selected_face_summaries))
        )
        if face_record.get("face") is not None:
            usable_face_records.append(face_record)

    if not usable_face_records:
        return make_result(
            selected_family_count=len(families),
            selected_face_summary={
                "pickKind": pick_kind,
                "requestedSelectionCount": len(face_references),
                "usableFaceCount": 0,
                "preselectHighlightUpdated": preselect_highlight_updated,
                "preselectRefreshed": preselect_refreshed,
                "preselectShown": preselect_shown,
                "useFastMode": use_fast_mode,
            },
            selected_face_summaries=selected_face_summaries,
            message="The picked references did not resolve to usable faces.",
        )

    selected_face_summary = {
        "pickKind": pick_kind,
        "requestedSelectionCount": len(face_references),
        "usableFaceCount": len(usable_face_records),
        "maxVerticalMoveMm": float(max_vertical_move_mm),
        "preselectHighlightUpdated": preselect_highlight_updated,
        "preselectRefreshed": preselect_refreshed,
        "preselectShown": preselect_shown,
        "useFastMode": use_fast_mode,
    }

    highlighted_elements = []
    highlighted_elements.extend(families)
    for face_record in usable_face_records:
        highlighted_elements.append(face_record.get("hostElement"))

    moved_ids = []
    move_details = []
    skipped = []
    traced_model_curve_ids = []
    traced_sketch_plane_id = None
    helper_directshape_id = None
    helper_directshape_ids = []
    object_marker_ids = []
    helper_directshape_error = None

    TransactionManager.Instance.EnsureInTransaction(doc)
    try:
        for family in families:
            source_point = get_instance_point(family)
            attachment_anchor_point = get_instance_bottom_center(family)
            family_bottom_z, family_top_z = get_instance_vertical_bounds(family)

            if source_point is None or attachment_anchor_point is None or family_bottom_z is None or family_top_z is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "reason": "The selected element does not expose a usable point or bounding box.",
                    }
                )
                continue

            evaluation_bottom_points = get_instance_bottom_corners(family)
            evaluation_source_point = attachment_anchor_point

            best_face_record, target_point, xy_distance, target_mode = get_best_face_target_for_family(
                usable_face_records,
                evaluation_source_point,
                evaluation_bottom_points,
            )
            if target_point is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "selectionMode": "multiFaceSelection",
                        "reason": "The selected face did not provide a usable local point.",
                    }
                )
                continue

            host_element = best_face_record.get("hostElement")
            linked_element = best_face_record.get("linkedElement")
            vertical_delta = target_point.Z - attachment_anchor_point.Z
            move_distance_sq = vertical_delta * vertical_delta

            if abs(vertical_delta) > max_vertical_move_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "xyDistanceMm": round(from_internal_mm(xy_distance), 2)
                        if xy_distance is not None
                        else None,
                        "verticalDeltaMm": round(from_internal_mm(vertical_delta), 2),
                        "targetMode": target_mode,
                        "selectedFaceIndex": usable_face_records.index(best_face_record),
                        "selectionMode": best_face_record.get("selectionMode"),
                        "reason": "Skipped because the computed Z move is too large.",
                    }
                )
                continue

            if move_distance_sq < (min_vertical_gap_internal * min_vertical_gap_internal):
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "xyDistanceMm": round(from_internal_mm(xy_distance), 2)
                        if xy_distance is not None
                        else None,
                        "verticalDeltaMm": round(from_internal_mm(vertical_delta), 2),
                        "targetMode": target_mode,
                        "selectedFaceIndex": usable_face_records.index(best_face_record),
                        "reason": "The selected face is already at the same Z as the element bottom.",
                    }
                )
                continue

            sub_transaction = SubTransaction(doc)
            try:
                sub_transaction.Start()
                ElementTransformUtils.MoveElement(
                    doc,
                    family.Id,
                    XYZ(0.0, 0.0, vertical_delta),
                )
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
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
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
                    "hostType": type(host_element).__name__ if host_element is not None else None,
                    "linkedElementId": safe_int_id(linked_element),
                    "linkedElementType": type(linked_element).__name__ if linked_element is not None else None,
                    "sourcePoint": xyz_to_list(source_point),
                    "attachmentAnchorPoint": xyz_to_list(attachment_anchor_point),
                    "targetPoint": xyz_to_list(target_point),
                    "sourceBottomZMm": round(from_internal_mm(family_bottom_z), 2),
                    "sourceTopZMm": round(from_internal_mm(family_top_z), 2),
                    "xyDistanceMm": round(from_internal_mm(xy_distance), 2)
                    if xy_distance is not None
                    else None,
                    "movedDeltaXMm": 0.0,
                    "movedDeltaYMm": 0.0,
                    "movedDeltaZMm": round(from_internal_mm(vertical_delta), 2),
                    "targetMode": target_mode,
                    "selectedFaceIndex": usable_face_records.index(best_face_record),
                    "selectionMode": best_face_record.get("selectionMode"),
                    "status": "movedToSelectedFace",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            moved_ids=moved_ids,
            move_details=move_details,
            skipped=skipped,
            selected_face_summary=selected_face_summary,
            selected_face_summaries=selected_face_summaries,
            message="The run stopped because of an unexpected error.",
            unhandled_error=str(unhandled_error),
            traced_model_curve_ids=traced_model_curve_ids,
            traced_sketch_plane_id=traced_sketch_plane_id,
            helper_directshape_id=helper_directshape_id,
            helper_directshape_ids=helper_directshape_ids,
            object_marker_ids=object_marker_ids,
            deleted_helper_ids=[],
        )
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    selection_highlight_updated = False
    selection_shown = False
    selected_face_summary["selectionHighlightUpdated"] = selection_highlight_updated
    selected_face_summary["selectionShown"] = selection_shown

    return make_result(
        selected_family_count=len(families),
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        selected_face_summary=selected_face_summary,
        selected_face_summaries=selected_face_summaries,
        message="Completed. The selected elements were moved in Z to the best matching picked face.",
        traced_model_curve_ids=traced_model_curve_ids,
        traced_sketch_plane_id=traced_sketch_plane_id,
        helper_directshape_id=helper_directshape_id,
        helper_directshape_ids=helper_directshape_ids,
        object_marker_ids=object_marker_ids,
        deleted_helper_ids=[],
    )


OUT = main()
