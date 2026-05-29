import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    ElementTransformUtils,
    FamilyInstance,
    LocationPoint,
    ModelCurve,
    ReferencePlane,
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
    """XYZ 좌표를 Watch에서 보기 좋은 리스트로 바꿉니다."""
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
    선택된 요소를 이동할 패밀리와 기준 호스트로 나눕니다.

    기준 호스트로 허용하는 요소:
    - ModelCurve
    - ReferencePlane
    """
    families = []
    hosts = []

    for element in elements:
        if isinstance(element, FamilyInstance):
            families.append(element)
        elif isinstance(element, ModelCurve) or isinstance(element, ReferencePlane):
            hosts.append(element)

    return families, hosts


def get_instance_point(instance):
    """패밀리 기준점(LocationPoint)을 가져옵니다."""
    location = instance.Location
    if isinstance(location, LocationPoint):
        return location.Point
    return None


def get_bottom_z(instance):
    """
    패밀리 하단 Z 값을 구합니다.

    장비를 선이나 평면에 '닿게' 하려면
    기준점이 아니라 패밀리 하단 높이를 알아야 하므로 BoundingBox.Min.Z를 씁니다.
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


def get_modelcurve_target(host_element, source_point):
    """ModelCurve에 source_point를 투영해서 최근접 점을 구합니다."""
    try:
        curve = host_element.GeometryCurve
    except Exception:
        curve = None

    if curve is None:
        return None, None

    try:
        projection = curve.Project(source_point)
    except Exception:
        projection = None

    if projection is None:
        return None, None

    try:
        target_point = projection.XYZPoint
    except Exception:
        target_point = None

    if target_point is None:
        return None, None

    try:
        dx = target_point.X - source_point.X
        dy = target_point.Y - source_point.Y
        distance = (dx ** 2 + dy ** 2) ** 0.5
    except Exception:
        distance = None

    return target_point, distance


def get_referenceplane_target(host_element, source_point):
    """
    ReferencePlane 위에서 현재 장비 XY에 대응되는 점을 구합니다.

    초보자용 설명:
    - ReferencePlane은 무한 평면처럼 생각하면 됩니다.
    - 현재 장비의 X,Y는 유지하고, 그 위치에서 평면의 Z를 계산합니다.
    """
    try:
        plane = host_element.GetPlane()
    except Exception:
        plane = None

    if plane is None:
        return None, None

    try:
        normal = plane.Normal
        origin = plane.Origin
    except Exception:
        return None, None

    if normal is None or origin is None:
        return None, None

    # 평면 법선의 Z가 0에 너무 가까우면, 현재 XY에서 Z를 계산할 수 없습니다.
    try:
        if abs(normal.Z) < 1e-9:
            return None, None
    except Exception:
        return None, None

    try:
        target_z = origin.Z - (
            normal.X * (source_point.X - origin.X) +
            normal.Y * (source_point.Y - origin.Y)
        ) / normal.Z
        target_point = XYZ(source_point.X, source_point.Y, target_z)
    except Exception:
        return None, None

    return target_point, 0.0


def get_host_target(host_element, source_point):
    """호스트 종류에 따라 목표점을 구합니다."""
    if isinstance(host_element, ModelCurve):
        target_point, distance = get_modelcurve_target(host_element, source_point)
        return "ModelCurve", target_point, distance

    if isinstance(host_element, ReferencePlane):
        target_point, distance = get_referenceplane_target(host_element, source_point)
        return "ReferencePlane", target_point, distance

    return "UnknownHost", None, None


def make_result(
    selected_family_count=0,
    selected_host_count=0,
    moved_ids=None,
    move_details=None,
    skipped=None,
    message="",
    unhandled_error=None,
    host_summary=None,
    invalid_source_ids=None,
):
    """Watch에 보기 좋은 결과 딕셔너리를 만듭니다."""
    return {
        "selectedFamilyCount": selected_family_count,
        "selectedHostCount": selected_host_count,
        "movedIds": moved_ids or [],
        "moveDetails": move_details or [],
        "skipped": skipped or [],
        "hostSummary": host_summary or {},
        "invalidSourceIds": invalid_source_ids or [],
        "message": message,
        "unhandledError": unhandled_error,
    }


def main():
    max_distance_internal = to_internal_mm(to_float(max_distance_mm, 5000.0))

    selected_elements = get_selected_elements()
    families, hosts = split_selection(selected_elements)

    invalid_source_ids = []
    valid_families = []

    for family in families:
        if get_instance_point(family) is None:
            invalid_source_ids.append(safe_int_id(family))
            continue
        valid_families.append(family)

    if len(hosts) != 1:
        return make_result(
            selected_family_count=len(families),
            selected_host_count=len(hosts),
            invalid_source_ids=invalid_source_ids,
            message="Select exactly one host element (ModelCurve or ReferencePlane) and one or more family instances.",
        )

    if not valid_families:
        return make_result(
            selected_family_count=len(families),
            selected_host_count=1,
            invalid_source_ids=invalid_source_ids,
            message="No valid family instances with a LocationPoint were selected.",
        )

    host_element = hosts[0]
    if isinstance(host_element, ModelCurve):
        host_type = "ModelCurve"
    elif isinstance(host_element, ReferencePlane):
        host_type = "ReferencePlane"
    else:
        host_type = "UnknownHost"

    host_summary = {
        "selectedHostId": safe_int_id(host_element),
        "selectedHostType": host_type,
    }

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

            host_type, target_point, distance = get_host_target(host_element, source_point)

            if target_point is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(host_element),
                        "hostType": host_type,
                        "sourcePoint": xyz_to_list(source_point),
                        "reason": "Could not calculate a target point on the selected host.",
                    }
                )
                continue

            if distance is not None and distance > max_distance_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(host_element),
                        "hostType": host_type,
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "reason": "The target point is outside the search distance.",
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
                        "hostId": safe_int_id(host_element),
                        "hostType": host_type,
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
                    "hostType": host_type,
                    "sourcePoint": xyz_to_list(source_point),
                    "targetPoint": xyz_to_list(target_point),
                    "distanceMm": round(from_internal_mm(distance), 2) if distance is not None else None,
                    "movedDeltaZMm": round(from_internal_mm(delta_z), 2),
                    "status": "movedOnZOnly",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            selected_host_count=1,
            moved_ids=moved_ids,
            move_details=move_details,
            skipped=skipped,
            host_summary=host_summary,
            invalid_source_ids=invalid_source_ids,
            message="The run stopped because of an unexpected error.",
            unhandled_error=str(unhandled_error),
        )
    finally:
        TransactionManager.Instance.TransactionTaskDone()

    return make_result(
        selected_family_count=len(families),
        selected_host_count=1,
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        host_summary=host_summary,
        invalid_source_ids=invalid_source_ids,
        message="Completed. The selected family instances were moved in Z using the selected host element.",
    )


OUT = main()
