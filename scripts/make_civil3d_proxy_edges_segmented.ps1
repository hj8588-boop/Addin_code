$ErrorActionPreference = 'Stop'

$src = (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter 'civil3d_dwg_outer_top_edge_proxy_selectable.dyn' | Select-Object -First 1).FullName
$dir = Split-Path -Parent $src
$backup = Join-Path $dir 'backup_civil3d_dwg_outer_top_edge_proxy_selectable.dyn'
$target = Join-Path $dir 'civil3d_dwg_outer_top_edge_proxy_segmented_visible.dyn'

Copy-Item -LiteralPath $src -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json
$py = $json.Nodes | Where-Object { $_.ConcreteType -like '*Python*' } | Select-Object -First 1
$code = [string]$py.Code

$oldBlock = @'
def flatten_curve_to_plane(curve, z):
    try:
        p0 = curve.GetEndPoint(0)
        p1 = curve.GetEndPoint(1)
        if isinstance(curve, Line):
            return Line.CreateBound(XYZ(p0.X, p0.Y, z), XYZ(p1.X, p1.Y, z))
        if hasattr(curve, "Center"):
            c = curve.Center
            x = curve.XDirection
            y = curve.YDirection
            return Arc.Create(XYZ(c.X, c.Y, z), curve.Radius, curve.GetEndParameter(0), curve.GetEndParameter(1), x, y)
    except:
        pass
    return None

def make_selectable_model_curves(run):
    created = []
    if not run:
        return created
    z = average_z(run)
    plane = Plane.CreateByOriginAndBasis(XYZ(0,0,z), XYZ.BasisX, XYZ.BasisY)
    skp = SketchPlane.Create(doc, plane)
    for c in run:
        flat = flatten_curve_to_plane(c, z)
        if flat is None:
            continue
        try:
            mc = doc.Create.NewModelCurve(flat, skp)
            created.append(mc)
        except:
            pass
    return created
'@

$newBlock = @'
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
    plane = Plane.CreateByOriginAndBasis(XYZ(0,0,z), XYZ.BasisX, XYZ.BasisY)
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

$code = $code.Replace($oldBlock, $newBlock)

$oldTail = @'
if _MAIN_ELEMENT is not None and _SIDE_ELEMENT is not None:
    main_run = get_primary_top_run_from_element(_MAIN_ELEMENT, tol)
    side_run = get_primary_top_run_from_element(_SIDE_ELEMENT, tol)

    TransactionManager.Instance.EnsureInTransaction(doc)
    created = []
    created.extend(make_selectable_model_curves(main_run))
    created.extend(make_selectable_model_curves(side_run))
    TransactionManager.Instance.TransactionTaskDone()
    OUT = created
elif 'OUT' not in globals():
    OUT = []
'@

$newTail = @'
if _MAIN_ELEMENT is not None and _SIDE_ELEMENT is not None:
    main_run = get_primary_top_run_from_element(_MAIN_ELEMENT, tol)
    side_run = get_primary_top_run_from_element(_SIDE_ELEMENT, tol)

    TransactionManager.Instance.EnsureInTransaction(doc)
    created_main = make_selectable_model_curves(main_run)
    created_side = make_selectable_model_curves(side_run)
    created = []
    created.extend(created_main)
    created.extend(created_side)
    TransactionManager.Instance.TransactionTaskDone()
    OUT = {
        "created_total": len(created),
        "created_main": len(created_main),
        "created_side": len(created_side),
        "main_run_curves": len(main_run),
        "side_run_curves": len(side_run),
        "elements": created
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
