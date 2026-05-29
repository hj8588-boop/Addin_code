$ErrorActionPreference = 'Stop'

$src = (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter 'civil3d_dwg_to_revit_outer_top_single_run_autoplace.dyn' | Select-Object -First 1).FullName
$dir = Split-Path -Parent $src
$backup = Join-Path $dir 'backup_civil3d_dwg_to_revit_outer_top_single_run_autoplace_for_merge.dyn'
$target = Join-Path $dir 'final_civil3d_dwg_outer_top_single_run_proxy_and_place.dyn'

Copy-Item -LiteralPath $src -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json
$py = $json.Nodes | Where-Object { $_.ConcreteType -like '*Python*' } | Select-Object -First 1
$code = [string]$py.Code

$anchor = @'
def rotate_instance_zonly(inst, target_x):
    current_x = get_instance_x_axis(inst)
    target_x = normalize_xy(target_x)
    if target_x is None:
        return
    pivot = get_instance_point(inst)
    angle = signed_angle_xy(current_x, target_x)
    if abs(angle) < 1e-6:
        return
    try:
        axis = Line.CreateBound(pivot, pivot + XYZ.BasisZ)
        ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)
    except:
        pass
'@

$insert = @'
def rotate_instance_zonly(inst, target_x):
    current_x = get_instance_x_axis(inst)
    target_x = normalize_xy(target_x)
    if target_x is None:
        return
    pivot = get_instance_point(inst)
    angle = signed_angle_xy(current_x, target_x)
    if abs(angle) < 1e-6:
        return
    try:
        axis = Line.CreateBound(pivot, pivot + XYZ.BasisZ)
        ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)
    except:
        pass

def average_z(run):
    vals = []
    for c in run:
        try:
            pts = list(c.Tessellate())
        except:
            pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
        for p in pts:
            vals.append(p.Z)
    if not vals:
        return 0.0
    return sum(vals) / float(len(vals))

def curve_points_for_proxy(curve):
    try:
        pts = list(curve.Tessellate())
    except:
        try:
            pts = [curve.GetEndPoint(0), curve.GetEndPoint(1)]
        except:
            pts = []
    clean = []
    for p in pts:
        if not clean or clean[-1].DistanceTo(p) > 1e-6:
            clean.append(p)
    return clean

def make_selectable_model_curves(run):
    created = []
    if not run:
        return created
    z = average_z(run)
    plane = Plane.CreateByOriginAndBasis(XYZ(0, 0, z), XYZ.BasisX, XYZ.BasisY)
    skp = SketchPlane.Create(doc, plane)
    for c in run:
        pts = curve_points_for_proxy(c)
        if len(pts) < 2:
            continue
        for i in range(len(pts) - 1):
            p0 = XYZ(pts[i].X, pts[i].Y, z)
            p1 = XYZ(pts[i + 1].X, pts[i + 1].Y, z)
            if p0.DistanceTo(p1) <= 1e-6:
                continue
            try:
                mc = doc.Create.NewModelCurve(Line.CreateBound(p0, p1), skp)
                created.append(mc)
            except:
                pass
    return created
'@

$code = $code.Replace($anchor, $insert)

$oldTail = @'
if _MAIN_ELEMENT is not None and _SIDE_ELEMENT is not None and symbol is not None and spacing > 0:
    main_run = get_primary_top_run_from_element(_MAIN_ELEMENT, tol)
    side_run = get_primary_top_run_from_element(_SIDE_ELEMENT, tol)
    pairs = []
    if main_run and side_run:
        pairs = [(main_run, side_run)]

    TransactionManager.Instance.EnsureInTransaction(doc)
    if not symbol.IsActive:
        symbol.Activate()

    instances = []
    origin_pt = XYZ.Zero

    for mr, sr in pairs:
        lens = [c.Length for c in mr]
        total = sum(lens)
        if total < 1e-6:
            continue

        cum = [0.0]
        acc = 0.0
        for L in lens:
            acc += L
            cum.append(acc)

        def locate(d):
            for i, L in enumerate(lens):
                if d <= cum[i + 1] + 1e-9:
                    t = (d - cum[i]) / L if L > 1e-9 else 0.0
                    return i, max(0.0, min(1.0, t))
            return len(lens) - 1, 1.0

        d = off_par
        if d < 0:
            d = 0.0
        if d > total:
            d = total

        while d <= total + 1e-9:
            idx, t = locate(d)
            c = mr[idx]

            target_pt = c.Evaluate(t, True)
            deriv = c.ComputeDerivatives(t, True)
            tanDir = deriv.BasisX.Normalize()

            best_pt = None
            best_d = None
            for sc in sr:
                pr = sc.Project(target_pt)
                if pr:
                    dd = pr.XYZPoint.DistanceTo(target_pt)
                    if best_d is None or dd < best_d:
                        best_d = dd
                        best_pt = pr.XYZPoint
            if not best_pt:
                d += spacing
                continue

            rawY = (best_pt - target_pt)
            rawY = rawY - tanDir.Multiply(rawY.DotProduct(tanDir))
            if rawY.GetLength() < 1e-6:
                d += spacing
                continue
            yDir = rawY.Normalize()

            zDir = tanDir.CrossProduct(yDir)
            if zDir.GetLength() < 1e-9:
                d += spacing
                continue
            xDir = yDir.CrossProduct(zDir)
            if xDir.GetLength() < 1e-9:
                d += spacing
                continue
            xDir = xDir.Normalize()

            if rotate_mode == 1:
                target_x = yDir
            elif rotate_mode == 2:
                target_x = xDir.Negate()
            else:
                target_x = xDir

            target_pt = target_pt + (yDir * off_perp)

            try:
                plane = Plane.CreateByOriginAndBasis(origin_pt, XYZ.BasisX, XYZ.BasisY)
                skp = SketchPlane.Create(doc, plane)
                view.SketchPlane = skp

                inst = doc.Create.NewFamilyInstance(
                    origin_pt, symbol, skp, StructuralType.NonStructural
                )
                current_pt = get_instance_point(inst)
                move_vec = target_pt - current_pt
                ElementTransformUtils.MoveElement(doc, inst.Id, move_vec)
                doc.Regenerate()
                rotate_instance_zonly(inst, target_x)
                instances.append(inst)
            except:
                pass

            d += spacing

    OUT = instances
    TransactionManager.Instance.TransactionTaskDone()
elif 'OUT' not in globals():
    OUT = []
'@

$newTail = @'
if _MAIN_ELEMENT is not None and _SIDE_ELEMENT is not None and symbol is not None and spacing > 0:
    main_run = get_primary_top_run_from_element(_MAIN_ELEMENT, tol)
    side_run = get_primary_top_run_from_element(_SIDE_ELEMENT, tol)
    pairs = []
    if main_run and side_run:
        pairs = [(main_run, side_run)]

    TransactionManager.Instance.EnsureInTransaction(doc)
    if not symbol.IsActive:
        symbol.Activate()

    proxy_main = make_selectable_model_curves(main_run)
    proxy_side = make_selectable_model_curves(side_run)

    instances = []
    origin_pt = XYZ.Zero

    for mr, sr in pairs:
        lens = [c.Length for c in mr]
        total = sum(lens)
        if total < 1e-6:
            continue

        cum = [0.0]
        acc = 0.0
        for L in lens:
            acc += L
            cum.append(acc)

        def locate(d):
            for i, L in enumerate(lens):
                if d <= cum[i + 1] + 1e-9:
                    t = (d - cum[i]) / L if L > 1e-9 else 0.0
                    return i, max(0.0, min(1.0, t))
            return len(lens) - 1, 1.0

        d = off_par
        if d < 0:
            d = 0.0
        if d > total:
            d = total

        while d <= total + 1e-9:
            idx, t = locate(d)
            c = mr[idx]

            target_pt = c.Evaluate(t, True)
            deriv = c.ComputeDerivatives(t, True)
            tanDir = deriv.BasisX.Normalize()

            best_pt = None
            best_d = None
            for sc in sr:
                pr = sc.Project(target_pt)
                if pr:
                    dd = pr.XYZPoint.DistanceTo(target_pt)
                    if best_d is None or dd < best_d:
                        best_d = dd
                        best_pt = pr.XYZPoint
            if not best_pt:
                d += spacing
                continue

            rawY = (best_pt - target_pt)
            rawY = rawY - tanDir.Multiply(rawY.DotProduct(tanDir))
            if rawY.GetLength() < 1e-6:
                d += spacing
                continue
            yDir = rawY.Normalize()

            zDir = tanDir.CrossProduct(yDir)
            if zDir.GetLength() < 1e-9:
                d += spacing
                continue
            xDir = yDir.CrossProduct(zDir)
            if xDir.GetLength() < 1e-9:
                d += spacing
                continue
            xDir = xDir.Normalize()

            if rotate_mode == 1:
                target_x = yDir
            elif rotate_mode == 2:
                target_x = xDir.Negate()
            else:
                target_x = xDir

            target_pt = target_pt + (yDir * off_perp)

            try:
                plane = Plane.CreateByOriginAndBasis(origin_pt, XYZ.BasisX, XYZ.BasisY)
                skp = SketchPlane.Create(doc, plane)
                view.SketchPlane = skp

                inst = doc.Create.NewFamilyInstance(
                    origin_pt, symbol, skp, StructuralType.NonStructural
                )
                current_pt = get_instance_point(inst)
                move_vec = target_pt - current_pt
                ElementTransformUtils.MoveElement(doc, inst.Id, move_vec)
                doc.Regenerate()
                rotate_instance_zonly(inst, target_x)
                instances.append(inst)
            except:
                pass

            d += spacing

    TransactionManager.Instance.TransactionTaskDone()
    OUT = {
        "proxy_main_count": len(proxy_main),
        "proxy_side_count": len(proxy_side),
        "placed_count": len(instances),
        "proxy_elements": proxy_main + proxy_side,
        "instances": instances
    }
elif 'OUT' not in globals():
    OUT = []
'@

$code = $code.Replace($oldTail, $newTail)
$py.Code = $code
$out = $json | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($target, $out, [System.Text.Encoding]::UTF8)
(Get-Item -LiteralPath $target).LastWriteTime = Get-Date
Get-Item -LiteralPath $backup, $target | Select-Object Name, Length, LastWriteTime
