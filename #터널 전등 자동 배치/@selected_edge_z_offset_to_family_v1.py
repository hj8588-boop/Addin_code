import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    Curve,
    ElementTransformUtils,
    FamilyInstance,
    GeometryInstance,
    ImportInstance,
    LocationPoint,
    Options,
    RevitLinkInstance,
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

min_vertical_gap_mm = 1.0


def get_input_value(index, default_value=None):
    try:
        if "IN" not in globals() or IN is None or len(IN) <= index:
            return default_value
        value = IN[index]
        if value is None:
            return default_value
        return value
    except Exception:
        return default_value


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


def get_selected_family_instances():
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


def get_element_geometry_options():
    options = Options()
    options.IncludeNonVisibleObjects = True
    options.ComputeReferences = False
    return options


def multiply_transforms(first, second):
    if first is None:
        return second
    if second is None:
        return first

    try:
        return first.Multiply(second)
    except Exception:
        try:
            return second.Multiply(first)
        except Exception:
            return second


def iter_curves_from_geometry(geometry_element, current_transform=None):
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
                for nested_curve, nested_transform in iter_curves_from_geometry(
                    instance_geometry,
                    geometry_transform,
                ):
                    yield nested_curve, nested_transform

            try:
                symbol_geometry = geometry_object.GetSymbolGeometry()
            except Exception:
                symbol_geometry = None

            if symbol_geometry is not None:
                for nested_curve, nested_transform in iter_curves_from_geometry(
                    symbol_geometry,
                    geometry_transform,
                ):
                    yield nested_curve, nested_transform
            continue

        if isinstance(geometry_object, Curve):
            yield geometry_object, current_transform


def get_global_curve(curve, transform=None):
    if curve is None:
        return None

    if transform is None:
        return curve

    try:
        return curve.CreateTransformed(transform)
    except Exception:
        return curve


def point_xy_distance(point_a, point_b):
    if point_a is None or point_b is None:
        return None

    try:
        dx = point_a.X - point_b.X
        dy = point_a.Y - point_b.Y
        return (dx * dx + dy * dy) ** 0.5
    except Exception:
        return None


def resolve_curve_from_reference(reference):
    if reference is None:
        return None

    try:
        element = doc.GetElement(reference.ElementId)
    except Exception:
        element = None

    if element is None:
        return None

    try:
        geometry_object = element.GetGeometryObjectFromReference(reference)
    except Exception:
        geometry_object = None

    if isinstance(geometry_object, Curve):
        return {
            "hostElement": element,
            "curve": geometry_object,
            "hostType": type(element).__name__,
            "linkedElement": None,
        }

    if isinstance(element, ImportInstance):
        geometry_options = get_element_geometry_options()
        try:
            geometry_element = element.get_Geometry(geometry_options)
        except Exception:
            geometry_element = None

        best_curve = None
        best_distance = None
        reference_point = None
        try:
            reference_point = reference.GlobalPoint
        except Exception:
            reference_point = None

        for raw_curve, raw_transform in iter_curves_from_geometry(geometry_element):
            global_curve = get_global_curve(raw_curve, raw_transform)
            if global_curve is None:
                continue

            if reference_point is None:
                best_curve = global_curve
                break

            try:
                projection = global_curve.Project(reference_point)
            except Exception:
                projection = None

            if projection is None:
                continue

            try:
                candidate_point = projection.XYZPoint
                distance = candidate_point.DistanceTo(reference_point)
            except Exception:
                continue

            if best_distance is None or distance < best_distance:
                best_curve = global_curve
                best_distance = distance

        if best_curve is not None:
            return {
                "hostElement": element,
                "curve": best_curve,
                "hostType": "ImportInstance",
                "linkedElement": None,
            }

    if isinstance(element, RevitLinkInstance):
        try:
            link_doc = element.GetLinkDocument()
        except Exception:
            link_doc = None

        linked_element = None
        try:
            linked_element = link_doc.GetElement(reference.LinkedElementId)
        except Exception:
            linked_element = None

        if linked_element is not None:
            try:
                linked_geometry_object = linked_element.GetGeometryObjectFromReference(reference)
            except Exception:
                linked_geometry_object = None

            if isinstance(linked_geometry_object, Curve):
                link_transform = None
                try:
                    link_transform = element.GetTransform()
                except Exception:
                    link_transform = None

                return {
                    "hostElement": element,
                    "curve": get_global_curve(linked_geometry_object, link_transform),
                    "hostType": "RevitLinkInstance",
                    "linkedElement": linked_element,
                }

    return None


def pick_edge_references():
    if uidoc is None:
        return None, "ActiveUIDocument is not available."

    try:
        references = uidoc.Selection.PickObjects(
            ObjectType.PointOnElement,
            "Select one or more DWG/Revit edges for light height control",
        )
        return list(references), None
    except Exception as pick_error:
        return None, str(pick_error)


def build_selected_edge_data(references):
    edge_data = []
    skipped = []

    for reference in references or []:
        resolved = resolve_curve_from_reference(reference)
        if resolved is None or resolved.get("curve") is None:
            skipped.append(
                {
                    "reason": "Could not resolve the selected reference to a usable curve.",
                    "elementId": safe_int_id(doc.GetElement(reference.ElementId))
                    if reference is not None
                    else None,
                }
            )
            continue

        curve = resolved["curve"]
        start_point = None
        end_point = None
        try:
            start_point = curve.GetEndPoint(0)
            end_point = curve.GetEndPoint(1)
        except Exception:
            pass

        edge_data.append(
            {
                "hostElement": resolved.get("hostElement"),
                "curve": curve,
                "hostType": resolved.get("hostType"),
                "linkedElement": resolved.get("linkedElement"),
                "startPoint": start_point,
                "endPoint": end_point,
            }
        )

    return edge_data, skipped


def find_best_edge_for_point(source_point, edge_data_list):
    best_match = None
    best_score = None

    for edge_data in edge_data_list:
        curve = edge_data.get("curve")
        if curve is None:
            continue

        try:
            projection = curve.Project(source_point)
        except Exception:
            projection = None

        if projection is None:
            continue

        try:
            target_point = projection.XYZPoint
            distance3d = target_point.DistanceTo(source_point)
            distance_xy = point_xy_distance(target_point, source_point)
            parameter = projection.Parameter
        except Exception:
            continue

        score = (
            distance_xy if distance_xy is not None else 1e18,
            distance3d,
        )

        if best_score is None or score < best_score:
            best_score = score
            best_match = {
                "edgeData": edge_data,
                "targetPoint": target_point,
                "distance3d": distance3d,
                "distanceXY": distance_xy,
                "parameter": parameter,
            }

    return best_match


def make_result(
    selected_family_count=0,
    selected_edge_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message=None,
    unhandled_error=None,
):
    return {
        "selectedFamilyCount": selected_family_count,
        "selectedEdgeCount": selected_edge_count,
        "movedIds": moved_ids or [],
        "moveDetails": move_details or [],
        "skipped": skipped or [],
        "message": message,
        "unhandledError": unhandled_error,
    }


def main():
    rerun_toggle = get_input_value(0, True)
    height_offset_mm = to_float(get_input_value(1, 0.0), 0.0)
    _ = rerun_toggle

    families = get_selected_family_instances()
    if not families:
        return make_result(
            selected_family_count=0,
            message="Select one or more family instances first, then run the graph and pick one or more edges.",
        )

    references, pick_error = pick_edge_references()
    if references is None:
        return make_result(
            selected_family_count=len(families),
            message="Edge selection was cancelled or failed.",
            unhandled_error=pick_error,
        )

    selected_edges, skipped = build_selected_edge_data(references)
    if not selected_edges:
        return make_result(
            selected_family_count=len(families),
            selected_edge_count=0,
            skipped=skipped,
            message="No usable edges were found from the selected references.",
        )

    moved_ids = []
    move_details = []
    min_vertical_gap_internal = to_internal_mm(min_vertical_gap_mm)
    offset_internal = to_internal_mm(height_offset_mm)

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
                        "reason": "The selected family does not expose a usable point or bounding box.",
                    }
                )
                continue

            best_match = find_best_edge_for_point(source_point, selected_edges)
            if best_match is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "reason": "No selected edge could be projected for this family.",
                    }
                )
                continue

            target_point = best_match["targetPoint"]
            target_z = target_point.Z + offset_internal
            vertical_delta = target_z - family_bottom_z

            if abs(vertical_delta) < min_vertical_gap_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "targetPoint": xyz_to_list(target_point),
                        "heightOffsetMm": height_offset_mm,
                        "verticalDeltaMm": round(from_internal_mm(vertical_delta), 2),
                        "reason": "The family is already at the requested edge height.",
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
                        "reason": "MoveElement failed.",
                        "error": str(move_error),
                    }
                )
                continue

            matched_edge = best_match["edgeData"]
            moved_ids.append(safe_int_id(family))
            move_details.append(
                {
                    "sourceId": safe_int_id(family),
                    "symbol": get_symbol_label(family),
                    "sourcePoint": xyz_to_list(source_point),
                    "sourceBottomZMm": round(from_internal_mm(family_bottom_z), 2),
                    "sourceTopZMm": round(from_internal_mm(family_top_z), 2),
                    "targetEdgeHostId": safe_int_id(matched_edge.get("hostElement")),
                    "targetEdgeLinkedElementId": safe_int_id(matched_edge.get("linkedElement")),
                    "targetEdgeHostType": matched_edge.get("hostType"),
                    "edgeStartPoint": xyz_to_list(matched_edge.get("startPoint")),
                    "edgeEndPoint": xyz_to_list(matched_edge.get("endPoint")),
                    "projectedPointOnEdge": xyz_to_list(target_point),
                    "heightOffsetMm": height_offset_mm,
                    "distanceXYMm": round(from_internal_mm(best_match["distanceXY"]), 2)
                    if best_match.get("distanceXY") is not None
                    else None,
                    "distance3DMm": round(from_internal_mm(best_match["distance3d"]), 2)
                    if best_match.get("distance3d") is not None
                    else None,
                    "movedDeltaZMm": round(from_internal_mm(vertical_delta), 2),
                    "status": "movedToSelectedEdgeHeight",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            selected_edge_count=len(selected_edges),
            moved_ids=moved_ids,
            move_details=move_details,
            skipped=skipped,
            message="The run stopped because of an unexpected error.",
            unhandled_error=str(unhandled_error),
        )
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    return make_result(
        selected_family_count=len(families),
        selected_edge_count=len(selected_edges),
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        message="Completed. The selected families were moved in Z using the selected edge heights.",
    )


OUT = main()
