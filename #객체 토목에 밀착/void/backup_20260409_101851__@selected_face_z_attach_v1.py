import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    ElementTransformUtils,
    Face,
    FamilyInstance,
    LocationPoint,
    SubTransaction,
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

# IN[0]: kept only for Dynamo graph compatibility
legacy_input = IN[0] if len(IN) > 0 else 0

min_vertical_gap_mm = 1
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


def get_selected_families():
    families = []

    for element in get_selected_elements():
        if isinstance(element, FamilyInstance):
            families.append(element)

    return families


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


def get_local_point_from_face(face, source_point):
    try:
        mesh = face.Triangulate()
    except Exception:
        mesh = None

    if mesh is None:
        return None, None

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

    if best_edge_point is None or best_edge_distance_sq is None:
        return None, None

    return best_edge_point, best_edge_distance_sq ** 0.5


def pick_face_reference():
    if uidoc is None:
        return None, "ActiveUIDocument is not available."

    try:
        reference = uidoc.Selection.PickObject(
            ObjectType.Face,
            "Select one host face for Z-only attachment",
        )
        return reference, None
    except Exception as pick_error:
        return None, str(pick_error)


def get_face_from_reference(reference):
    if reference is None:
        return None, None

    try:
        element = doc.GetElement(reference.ElementId)
    except Exception:
        element = None

    if element is None:
        return None, None

    try:
        geometry_object = element.GetGeometryObjectFromReference(reference)
    except Exception:
        geometry_object = None

    if isinstance(geometry_object, Face):
        return element, geometry_object

    return element, None


def make_result(
    selected_family_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message="",
    selected_face_summary=None,
    unhandled_error=None,
):
    return {
        "selectedFamilyCount": selected_family_count,
        "movedIds": moved_ids or [],
        "moveDetails": move_details or [],
        "skipped": skipped or [],
        "selectedFaceSummary": selected_face_summary or {},
        "message": message,
        "unhandledError": unhandled_error,
    }


def main():
    min_vertical_gap_internal = to_internal_mm(min_vertical_gap_mm)
    max_vertical_move_internal = to_internal_mm(max_vertical_move_mm)

    families = get_selected_families()
    if not families:
        return make_result(
            selected_family_count=0,
            message="Select one or more family instances first, then run the graph and pick a host face.",
        )

    face_reference, pick_error = pick_face_reference()
    if face_reference is None:
        return make_result(
            selected_family_count=len(families),
            message="Face selection was cancelled or failed.",
            unhandled_error=pick_error,
        )

    host_element, selected_face = get_face_from_reference(face_reference)
    if selected_face is None:
        return make_result(
            selected_family_count=len(families),
            message="The picked reference did not resolve to a usable face.",
        )

    selected_face_summary = {
        "hostId": safe_int_id(host_element),
        "hostType": type(host_element).__name__ if host_element is not None else None,
        "legacyInput": to_float(legacy_input, 0.0),
    }

    moved_ids = []
    move_details = []
    skipped = []

    TransactionManager.Instance.EnsureInTransaction(doc)
    try:
        for family in families:
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

            target_point, xy_distance = get_local_point_from_face(selected_face, source_point)
            if target_point is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "sourcePoint": xyz_to_list(source_point),
                        "reason": "The selected face did not provide a usable local point.",
                    }
                )
                continue

            vertical_delta = target_point.Z - family_bottom_z
            if abs(vertical_delta) < min_vertical_gap_internal:
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
                        "reason": "The selected face is already at the same Z as the family bottom.",
                    }
                )
                continue

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
                        "reason": "The selected face is too far away in Z, so the move was skipped for safety.",
                    }
                )
                continue

            sub_transaction = SubTransaction(doc)
            try:
                sub_transaction.Start()
                ElementTransformUtils.MoveElement(doc, family.Id, XYZ(0, 0, vertical_delta))
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
                    "sourcePoint": xyz_to_list(source_point),
                    "targetPoint": xyz_to_list(target_point),
                    "sourceBottomZMm": round(from_internal_mm(family_bottom_z), 2),
                    "sourceTopZMm": round(from_internal_mm(family_top_z), 2),
                    "xyDistanceMm": round(from_internal_mm(xy_distance), 2)
                    if xy_distance is not None
                    else None,
                    "movedDeltaZMm": round(from_internal_mm(vertical_delta), 2),
                    "status": "movedOnZOnlyToSelectedFace",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            moved_ids=moved_ids,
            move_details=move_details,
            skipped=skipped,
            selected_face_summary=selected_face_summary,
            message="The run stopped because of an unexpected error.",
            unhandled_error=str(unhandled_error),
        )
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    return make_result(
        selected_family_count=len(families),
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        selected_face_summary=selected_face_summary,
        message="Completed. The selected family instances were moved in Z to the picked face.",
    )


OUT = main()
