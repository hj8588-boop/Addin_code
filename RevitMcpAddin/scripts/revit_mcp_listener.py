"""
Revit MCP Listener - Dynamo Python Script 노드에 붙여넣기
이 스크립트를 Dynamo에서 "Run Periodically" 또는 버튼으로 실행하세요.

역할: C:\revit_mcp\command.json을 감시하다가 명령이 오면 실행하고
      결과를 C:\revit_mcp\result.json에 저장합니다.
"""

try:
    import clr
    clr.AddReference('RevitAPI')
    clr.AddReference('RevitAPIUI')
    clr.AddReference('RevitServices')

    from Autodesk.Revit.DB import *
    from Autodesk.Revit.DB import Structure
    from RevitServices.Persistence import DocumentManager
    from RevitServices.Transactions import TransactionManager

    import json
    import os
    import io

    COMM_DIR = r"C:\revit_mcp"
    COMMAND_FILE = os.path.join(COMM_DIR, "command.json")
    RESULT_FILE = os.path.join(COMM_DIR, "result.json")

    doc = DocumentManager.Instance.CurrentDBDocument

    def handle_get_elements_by_category(args):
        category_name = args.get("category", "")
        max_count = args.get("max_count", 20)
        collector = FilteredElementCollector(doc).WhereElementIsNotElementType()
        results = []
        for el in collector:
            cat = el.Category
            if cat is None:
                continue
            if category_name.lower() in cat.Name.lower():
                params = {}
                for p in el.Parameters:
                    try:
                        val = p.AsValueString() or p.AsString() or str(p.AsDouble())
                        params[p.Definition.Name] = val
                    except:
                        pass
                results.append({
                    "id": el.Id.IntegerValue,
                    "category": cat.Name,
                    "name": el.Name if hasattr(el, 'Name') else "",
                    "parameters": params
                })
            if len(results) >= max_count:
                break
        return {"count": len(results), "elements": results}

    def handle_get_element_by_id(args):
        el_id = args.get("element_id")
        el = doc.GetElement(ElementId(int(el_id)))
        if el is None:
            return {"error": "ID {}에 해당하는 요소를 찾을 수 없습니다.".format(el_id)}
        params = {}
        for p in el.Parameters:
            try:
                val = p.AsValueString() or p.AsString() or str(p.AsDouble())
                params[p.Definition.Name] = val
            except:
                pass
        return {
            "id": el.Id.IntegerValue,
            "category": el.Category.Name if el.Category else "Unknown",
            "name": el.Name if hasattr(el, 'Name') else "",
            "parameters": params
        }

    def handle_set_parameter(args):
        el_id = args.get("element_id")
        param_name = args.get("parameter_name")
        value = args.get("value")
        el = doc.GetElement(ElementId(int(el_id)))
        if el is None:
            return {"success": False, "error": "ID {} 요소 없음".format(el_id)}
        param = el.LookupParameter(param_name)
        if param is None:
            return {"success": False, "error": "파라미터 '{}' 없음".format(param_name)}
        if param.IsReadOnly:
            return {"success": False, "error": "파라미터 '{}'는 읽기 전용입니다.".format(param_name)}
        try:
            TransactionManager.Instance.EnsureInTransaction(doc)
            storage = param.StorageType
            if storage == StorageType.String:
                param.Set(value)
            elif storage == StorageType.Double:
                param.Set(float(value))
            elif storage == StorageType.Integer:
                param.Set(int(value))
            TransactionManager.Instance.ForceCloseTransaction()
            return {"success": True, "message": "'{}' → '{}' 설정 완료".format(param_name, value)}
        except Exception as e:
            return {"success": False, "error": str(e)}

    def handle_get_project_info(args):
        info = doc.ProjectInformation
        return {
            "project_name": info.Name,
            "project_number": info.Number,
            "client_name": info.ClientName,
            "address": info.Address,
            "author": info.Author,
            "building_name": info.BuildingName,
            "organization_name": info.OrganizationName,
        }

    def handle_get_all_categories(args):
        cats = []
        collector = FilteredElementCollector(doc).WhereElementIsNotElementType()
        seen = set()
        for el in collector:
            if el.Category and el.Category.Name not in seen:
                seen.add(el.Category.Name)
                cats.append(el.Category.Name)
        return {"categories": sorted(cats)}

    def handle_run_dynamo_script(args):
        return {"error": "Dynamo 스크립트 실행은 수동으로만 가능합니다. 대신 Dynamo Player를 사용하세요."}

    def handle_create_walls_with_windows(args):
        count      = int(args.get("count", 3))
        height_mm  = float(args.get("height_mm", 3000))
        length_mm  = float(args.get("length_mm", 5000))
        spacing_mm = float(args.get("spacing_mm", 2000))

        # Revit 내부 단위는 피트 — mm를 피트로 변환 (1피트 = 304.8mm)
        height_ft  = height_mm  / 304.8
        length_ft  = length_mm  / 304.8
        spacing_ft = spacing_mm / 304.8

        # 첫 번째 레벨 가져오기
        levels = FilteredElementCollector(doc).OfClass(Level).ToElements()
        if not levels:
            return {"error": "프로젝트에 레벨이 없습니다."}
        level = levels[0]

        # 첫 번째 기본 벽 타입 가져오기
        wall_types = FilteredElementCollector(doc).OfClass(WallType).ToElements()
        if not wall_types:
            return {"error": "프로젝트에 벽 타입이 없습니다."}
        wall_type = wall_types[0]

        # 창문 패밀리 심볼 가져오기
        win_symbols = FilteredElementCollector(doc).OfClass(FamilySymbol)\
                        .OfCategory(BuiltInCategory.OST_Windows).ToElements()
        has_window = len(win_symbols) > 0
        if has_window:
            win_symbol = win_symbols[0]

        TransactionManager.Instance.EnsureInTransaction(doc)

        created = []
        for i in range(count):
            # 각 벽을 X 방향으로 나란히 배치
            x_start = i * (length_ft + spacing_ft)
            pt1 = XYZ(x_start, 0, 0)
            pt2 = XYZ(x_start + length_ft, 0, 0)
            line = Line.CreateBound(pt1, pt2)

            wall = Wall.Create(doc, line, wall_type.Id, level.Id, height_ft, 0.0, False, False)
            wall_info = {"wall_id": wall.Id.IntegerValue, "wall_type": wall_type.Name}

            # 창문 삽입 (각 벽 중앙)
            if has_window:
                if not win_symbol.IsActive:
                    win_symbol.Activate()
                mid = XYZ(x_start + length_ft / 2.0, 0, 0)
                win_inst = doc.Create.NewFamilyInstance(
                    mid, win_symbol, wall, level,
                    Structure.StructuralType.NonStructural
                )
                wall_info["window_id"] = win_inst.Id.IntegerValue
                wall_info["window_type"] = win_symbol.Family.Name
            else:
                wall_info["window_id"] = None
                wall_info["window_note"] = "창문 패밀리 없음 — Revit에서 창문 패밀리를 로드하세요."

            created.append(wall_info)

        TransactionManager.Instance.ForceCloseTransaction()

        return {
            "success": True,
            "level": level.Name,
            "wall_type": wall_type.Name,
            "height_mm": height_mm,
            "count": count,
            "walls": created,
        }

    HANDLERS = {
        "get_elements_by_category": handle_get_elements_by_category,
        "get_element_by_id": handle_get_element_by_id,
        "set_parameter": handle_set_parameter,
        "get_project_info": handle_get_project_info,
        "get_all_categories": handle_get_all_categories,
        "run_dynamo_script": handle_run_dynamo_script,
        "create_walls_with_windows": handle_create_walls_with_windows,
    }

    # --- 메인 실행 ---
    if not os.path.exists(COMM_DIR):
        os.makedirs(COMM_DIR)
    output_message = "대기 중... (command.json 없음)"

    if os.path.exists(COMMAND_FILE):
        try:
            with io.open(COMMAND_FILE, "r", encoding="utf-8") as f:
                cmd = json.load(f)
            os.remove(COMMAND_FILE)

            tool = cmd.get("tool", "")
            args = cmd.get("args", {})
            handler = HANDLERS.get(tool)

            if handler:
                result = handler(args)
            else:
                result = {"error": "알 수 없는 명령: {}".format(tool)}

            with io.open(RESULT_FILE, "w", encoding="utf-8") as f:
                json.dump(result, f, ensure_ascii=False, indent=2)

            output_message = "명령 실행 완료: {}".format(tool)

        except Exception as e:
            error_result = {"error": str(e)}
            with io.open(RESULT_FILE, "w", encoding="utf-8") as f:
                json.dump(error_result, f, ensure_ascii=False, indent=2)
            output_message = "오류 발생: {}".format(e)

    OUT = output_message

except Exception as _top_err:
    OUT = "오류: " + str(_top_err)
