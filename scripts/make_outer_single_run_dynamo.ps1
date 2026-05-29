$ErrorActionPreference = 'Stop'

$dir = Split-Path -Parent (
    (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter 'civil3d_dwg_to_revit_outer_top_edge_autoplace.dyn' | Select-Object -First 1).FullName
)
$source = Join-Path $dir 'civil3d_dwg_to_revit_outer_top_edge_autoplace.dyn'
$backup = Join-Path $dir 'backup_civil3d_dwg_to_revit_outer_top_edge_autoplace.dyn'
$target = Join-Path $dir 'civil3d_dwg_to_revit_outer_top_single_run_autoplace.dyn'

Copy-Item -LiteralPath $source -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($source, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json
$py = $json.Nodes | Where-Object { $_.ConcreteType -like '*Python*' } | Select-Object -First 1
$code = [string]$py.Code

$oldFunc = @'
def get_top_curves_from_element(element):
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = True
    curves = []
    transform = get_element_transform(element)
    try:
        geom = element.get_Geometry(opts)
    except:
        geom = None
    if geom is None:
        return []
    for g in geom:
        collect_curves_from_geometry(g, transform, curves)
    if not curves:
        return []
    z_values = []
    for c in curves:
        try:
            pts = list(c.Tessellate())
        except:
            pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
        if pts:
            z_values.append(max([p.Z for p in pts]))
    if not z_values:
        return []
    top_z = max(z_values)
    result = []
    for c in curves:
        try:
            pts = list(c.Tessellate())
        except:
            pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
        if pts and abs(max([p.Z for p in pts]) - top_z) <= top_z_tol:
            result.append(c)
    return result
'@

$newFunc = @'
def get_top_curves_from_element(element):
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = True
    curves = []
    transform = get_element_transform(element)
    try:
        geom = element.get_Geometry(opts)
    except:
        geom = None
    if geom is None:
        return []
    for g in geom:
        collect_curves_from_geometry(g, transform, curves)
    if not curves:
        return []
    z_values = []
    for c in curves:
        try:
            pts = list(c.Tessellate())
        except:
            pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
        if pts:
            z_values.append(max([p.Z for p in pts]))
    if not z_values:
        return []
    top_z = max(z_values)
    result = []
    for c in curves:
        try:
            pts = list(c.Tessellate())
        except:
            pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
        if pts and abs(max([p.Z for p in pts]) - top_z) <= top_z_tol:
            result.append(c)
    return result

def run_length(run):
    return sum([c.Length for c in run]) if run else 0.0

def get_primary_top_run_from_element(element, tol):
    top_curves = get_top_curves_from_element(element)
    if not top_curves:
        return []
    runs = build_runs(top_curves, tol)
    if not runs:
        return []
    best_run = None
    best_len = None
    for run in runs:
        total = run_length(run)
        if best_len is None or total > best_len:
            best_len = total
            best_run = run
    return best_run if best_run else []
'@

$code = $code.Replace($oldFunc, $newFunc)
$code = $code.Replace('    main_curves = get_top_curves_from_element(_MAIN_ELEMENT)
    side_curves = get_top_curves_from_element(_SIDE_ELEMENT)
    main_runs = build_runs(main_curves, tol)
    side_runs = build_runs(side_curves, tol)
    pairs = match_runs(main_runs, side_runs)', '    main_run = get_primary_top_run_from_element(_MAIN_ELEMENT, tol)
    side_run = get_primary_top_run_from_element(_SIDE_ELEMENT, tol)
    pairs = []
    if main_run and side_run:
        pairs = [(main_run, side_run)]')

$py.Code = $code
$out = $json | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($target, $out, [System.Text.Encoding]::UTF8)
(Get-Item -LiteralPath $target).LastWriteTime = Get-Date
Get-Item -LiteralPath $backup, $target | Select-Object Name, Length, LastWriteTime
