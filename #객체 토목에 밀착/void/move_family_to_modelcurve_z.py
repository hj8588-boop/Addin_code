import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    ElementTransformUtils,
    FamilyInstance,
    FilteredElementCollector,
    LocationPoint,
    ModelCurve,
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
    """숫자로 바꿀 수 있으면 숫자로 쓰고, 아니면 기본값을 씁니다."""
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
    """요소 ID를 Watch에서 보기 쉬운 정수로 돌려줍니다."""
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
    """패밀리와 타입 이름을 한 줄로 묶어서 보여줍니다."""
    try:
        symbol = instance.Symbol
        family_name = symbol.Family.Name
        type_name = symbol.Name
        return "{0} : {1}".format(family_name, type_name)
    except Exception:
        return "UnknownFamily : UnknownType"


def get_selected_elements():
    """현재 Revit에서 선택된 요소를 가져옵니다."""
    if uidoc is None:
        return []

    return [doc.GetElement(element_id) for element_id in uidoc.Selection.GetElementIds()]


def split_selection(elements):
    """
    선택된 요소를 장비 패밀리와 ModelCurve로 나눕니다.

    사용 방법:
    - 이동할 FamilyInstance를 하나 이상 선택
    - 기준이 될 ModelCurve를 정확히 하나 선택
    """
    families = []
    model_curves = []

    for element in elements:
        if isinstance(element, FamilyInstance):
            families.append(element)
        elif isinstance(element, ModelCurve):
            model_curves.append(element)

    return families, model_curves


def get_candidate_modelcurves():
    """
    자동 선택용 ModelCurve 후보를 모읍니다.

    우선순위:
    1. 현재 활성 뷰에 보이는 ModelCurve
    2. 현재 문서 전체 ModelCurve
    """
    candidates = []

    try:
        active_view = uidoc.ActiveView if uidoc else None
    except Exception:
        active_view = None

    if active_view is not None:
        try:
            view_curves = (
                FilteredElementCollector(doc, active_view.Id)
                .OfClass(ModelCurve)
                .ToElements()
            )
            candidates = list(view_curves)
        except Exception:
            candidates = []

    if candidates:
        return candidates, "activeView"

    try:
        all_curves = FilteredElementCollector(doc).OfClass(ModelCurve).ToElements()
        return list(all_curves), "document"
    except Exception:
        return [], "document"


def get_instance_point(instance):
    """패밀리의 기준점(LocationPoint)을 가져옵니다."""
    location = instance.Location
    if isinstance(location, LocationPoint):
        return location.Point
    return None


def get_bottom_z(instance):
    """
    패밀리의 하단 Z 값을 구합니다.

    초보자용 설명:
    - 장비를 선의 높이에 '닿게' 하려면
      패밀리 기준점이 아니라 패밀리 바닥 높이를 알아야 합니다.
    - 그래서 BoundingBox의 Min.Z를 사용합니다.
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


def get_modelcurve_projection(curve, source_point):
    """
    현재 장비 위치를 ModelCurve에 투영해서 가장 가까운 점을 구합니다.

    반환값:
    - target_point: curve 위 최근접 점
    - distance: source_point와 target_point 사이 거리
    """
    if curve is None or source_point is None:
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
        delta_x = target_point.X - source_point.X
        delta_y = target_point.Y - source_point.Y
        distance = (delta_x ** 2 + delta_y ** 2) ** 0.5
    except Exception:
        distance = None

    return target_point, distance


def find_nearest_modelcurve(source_point, modelcurve_elements):
    """
    여러 ModelCurve 중에서 현재 패밀리와 XY 기준으로 가장 가까운 곡선을 찾습니다.
    """
    best_match = None
    best_distance = None

    for modelcurve_element in modelcurve_elements:
        try:
            host_curve = modelcurve_element.GeometryCurve
        except Exception:
            host_curve = None

        if host_curve is None:
            continue

        target_point, distance = get_modelcurve_projection(host_curve, source_point)
        if target_point is None or distance is None:
            continue

        if best_distance is None or distance < best_distance:
            best_distance = distance
            best_match = {
                "element": modelcurve_element,
                "curve": host_curve,
                "targetPoint": target_point,
                "distance": distance,
            }

    return best_match


def make_result(
    selected_family_count=0,
    selected_modelcurve_count=0,
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
        "selectedModelCurveCount": selected_modelcurve_count,
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
    families, model_curves = split_selection(selected_elements)

    invalid_source_ids = []
    valid_families = []

    for family in families:
        if get_instance_point(family) is None:
            invalid_source_ids.append(safe_int_id(family))
            continue
        valid_families.append(family)

    if not valid_families:
        return make_result(
            selected_family_count=len(families),
            selected_modelcurve_count=len(model_curves),
            invalid_source_ids=invalid_source_ids,
            message="No valid family instances with a LocationPoint were selected.",
        )

    selected_host_curve = None
    selection_mode = None
    auto_selection_scope = None

    if len(model_curves) == 1:
        selected_host_curve = model_curves[0]
        selection_mode = "selected"
    elif len(model_curves) > 1:
        return make_result(
            selected_family_count=len(families),
            selected_modelcurve_count=len(model_curves),
            invalid_source_ids=invalid_source_ids,
            message="Select only one ModelCurve, or select no ModelCurve to use automatic nearest search.",
        )

    if selected_host_curve is not None:
        try:
            host_curve = selected_host_curve.GeometryCurve
        except Exception:
            host_curve = None

        if host_curve is None:
            return make_result(
                selected_family_count=len(families),
                selected_modelcurve_count=1,
                invalid_source_ids=invalid_source_ids,
                host_summary={
                    "selectedHostId": safe_int_id(selected_host_curve),
                    "selectedHostType": "ModelCurve",
                    "selectionMode": "selected",
                },
                message="The selected ModelCurve does not expose usable geometry.",
            )

    auto_modelcurves = []
    if selected_host_curve is None:
        auto_modelcurves, auto_selection_scope = get_candidate_modelcurves()
        if not auto_modelcurves:
            return make_result(
                selected_family_count=len(families),
                selected_modelcurve_count=0,
                invalid_source_ids=invalid_source_ids,
                host_summary={
                    "selectedHostType": "ModelCurve",
                    "selectionMode": "autoNearest",
                    "autoSelectionScope": auto_selection_scope,
                },
                message="No usable ModelCurve candidates were found for automatic search.",
            )
        selection_mode = "autoNearest"

    moved_ids = []
    move_details = []
    skipped = []

    host_summary = {
        "selectedHostType": "ModelCurve",
        "selectionMode": selection_mode,
        "autoSelectionScope": auto_selection_scope,
    }
    if selected_host_curve is not None:
        host_summary["selectedHostId"] = safe_int_id(selected_host_curve)

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

            if selected_host_curve is not None:
                host_curve_element = selected_host_curve
                target_point, distance = get_modelcurve_projection(host_curve, source_point)
            else:
                nearest_match = find_nearest_modelcurve(source_point, auto_modelcurves)
                if nearest_match is None:
                    host_curve_element = None
                    target_point = None
                    distance = None
                else:
                    host_curve_element = nearest_match["element"]
                    target_point = nearest_match["targetPoint"]
                    distance = nearest_match["distance"]

            if target_point is None or distance is None or host_curve_element is None:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(host_curve_element) if host_curve_element else None,
                        "sourcePoint": xyz_to_list(source_point),
                        "reason": "Could not find a usable projected point on a ModelCurve.",
                    }
                )
                continue

            if distance > max_distance_internal:
                skipped.append(
                    {
                        "sourceId": safe_int_id(family),
                        "symbol": get_symbol_label(family),
                        "hostId": safe_int_id(host_curve_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
                        "reason": "The projected ModelCurve point is outside the search distance.",
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
                        "hostId": safe_int_id(host_curve_element),
                        "sourcePoint": xyz_to_list(source_point),
                        "targetPoint": xyz_to_list(target_point),
                        "distanceMm": round(from_internal_mm(distance), 2),
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
                    "hostId": safe_int_id(host_curve_element),
                    "hostType": "ModelCurve",
                    "sourcePoint": xyz_to_list(source_point),
                    "targetPoint": xyz_to_list(target_point),
                    "distanceMm": round(from_internal_mm(distance), 2),
                    "movedDeltaZMm": round(from_internal_mm(delta_z), 2),
                    "status": "movedOnZOnly",
                }
            )
    except Exception as unhandled_error:
        return make_result(
            selected_family_count=len(families),
            selected_modelcurve_count=1,
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
        selected_modelcurve_count=1,
        moved_ids=moved_ids,
        move_details=move_details,
        skipped=skipped,
        host_summary=host_summary,
        invalid_source_ids=invalid_source_ids,
        message="Completed. The selected family instances were moved in Z using the selected ModelCurve.",
    )


OUT = main()
