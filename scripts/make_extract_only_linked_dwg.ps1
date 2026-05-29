$ErrorActionPreference = 'Stop'

$src = (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter '@final_civil3d_dwg_explode_outer_top_single_run_extract_only.dyn' | Select-Object -First 1).FullName
$dir = Split-Path -Parent $src
$backup = Join-Path $dir 'backup_@final_civil3d_dwg_explode_outer_top_single_run_extract_only.dyn'
$target = Join-Path $dir '@final_civil3d_dwg_explode_outer_top_single_run_extract_only_linked.dyn'

Copy-Item -LiteralPath $src -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json
$pythonNode = $json.Nodes | Where-Object { $_.ConcreteType -like '*Python*' } | Select-Object -First 1
$pythonNode.Code = @'
# -*- coding: utf-8 -*-
import clr

clr.AddReference("RevitServices")
clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")

from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager
from Autodesk.Revit.DB import *
from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI import Selection

doc   = DocumentManager.Instance.CurrentDBDocument
uidoc = DocumentManager.Instance.CurrentUIApplication.ActiveUIDocument

repick = IN[0]
tol_mm = IN[1]
tol = float(tol_mm) / 304.8
top_z_tol = 1.0 / 304.8

def dist(a, b):
    return a.DistanceTo(b)

def reverse_curve(c):
    try:
        rc = c.Clone()
        rc.Reverse()
        return rc
    except:
        try:
            return c.CreateReversed()
        except:
            return c

def safe_multiply_transform(a, b):
    try:
        return a.Multiply(b)
    except:
        return a

def get_import_like_transform(element):
    try:
        if isinstance(element, ImportInstance):
            return element.GetTotalTransform()
    except:
        pass
    try:
        return element.GetTransform()
    except:
        return Transform.Identity

def transform_xyz(transform, point):
    try:
        return transform.OfPoint(point)
    except:
        return point

def transform_curve(transform, curve):
    try:
        return curve.CreateTransformed(transform)
    except:
        return curve

def collect_curves_from_geometry(geom_obj, transform, curves_out):
    if geom_obj is None:
        return
    try:
        curve = geom_obj.AsCurve()
        if curve is not None:
            curves_out.append(transform_curve(transform, curve))
            return
    except:
        pass
    try:
        if isinstance(geom_obj, PolyLine):
            pts = list(geom_obj.GetCoordinates())
            for i in range(len(pts) - 1):
                p0 = transform_xyz(transform, pts[i])
                p1 = transform_xyz(transform, pts[i + 1])
                if p0.DistanceTo(p1) > 1e-9:
                    curves_out.append(Line.CreateBound(p0, p1))
            return
    except:
        pass
    try:
        if isinstance(geom_obj, GeometryInstance):
            inst_transform = safe_multiply_transform(transform, geom_obj.Transform)
            for g in geom_obj.GetInstanceGeometry():
                collect_curves_from_geometry(g, inst_transform, curves_out)
            return
    except:
        pass
    try:
        if isinstance(geom_obj, GeometryElement):
            for g in geom_obj:
                collect_curves_from_geometry(g, transform, curves_out)
    except:
        pass

def get_top_curves_from_geometry_element(geom, transform):
    curves = []
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

def get_top_curves_from_element(element):
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = True
    try:
        geom = element.get_Geometry(opts)
    except:
        geom = None
    return get_top_curves_from_geometry_element(geom, get_import_like_transform(element))

def get_top_curves_from_pick(reference):
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = True

    host = doc.GetElement(reference.ElementId)
    if host is None:
        return []

    linked_id = None
    try:
        linked_id = reference.LinkedElementId
    except:
        linked_id = None

    try:
        has_linked = linked_id is not None and linked_id != ElementId.InvalidElementId and linked_id.IntegerValue != -1
    except:
        has_linked = False

    if isinstance(host, RevitLinkInstance) and has_linked:
        try:
            link_doc = host.GetLinkDocument()
            linked_el = link_doc.GetElement(linked_id)
            geom = linked_el.get_Geometry(opts)
            link_tr = host.GetTotalTransform()
            el_tr = get_import_like_transform(linked_el)
            total_tr = safe_multiply_transform(link_tr, el_tr)
            return get_top_curves_from_geometry_element(geom, total_tr)
        except:
            return []

    return get_top_curves_from_element(host)

def build_runs(curves, tol):
    segs = []
    for i, c in enumerate(curves):
        segs.append({"id": i, "curve": c, "p0": c.GetEndPoint(0), "p1": c.GetEndPoint(1), "used": False})
    runs = []

    def find_conn(pt):
        for s in segs:
            if s["used"]:
                continue
            if dist(pt, s["p0"]) <= tol:
                return s, 0
            if dist(pt, s["p1"]) <= tol:
                return s, 1
        return None, None

    for s in segs:
        if s["used"]:
            continue
        run = [s]
        s["used"] = True

        end = s["p1"]
        while True:
            ns, f = find_conn(end)
            if not ns:
                break
            c = ns["curve"]
            if f == 1:
                c = reverse_curve(c)
            ns["curve"] = c
            ns["p0"] = c.GetEndPoint(0)
            ns["p1"] = c.GetEndPoint(1)
            run.append(ns)
            ns["used"] = True
            end = ns["p1"]

        start = s["p0"]
        while True:
            ns, f = find_conn(start)
            if not ns:
                break
            c = ns["curve"]
            if f == 0:
                c = reverse_curve(c)
            ns["curve"] = c
            ns["p0"] = c.GetEndPoint(0)
            ns["p1"] = c.GetEndPoint(1)
            run.insert(0, ns)
            ns["used"] = True
            start = ns["p0"]

        runs.append([r["curve"] for r in run])
    return runs

def run_length(run):
    return sum([c.Length for c in run]) if run else 0.0

def get_primary_top_run_from_pick(reference, tol):
    top_curves = get_top_curves_from_pick(reference)
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
                created.append(doc.Create.NewModelCurve(Line.CreateBound(p0, p1), skp))
            except:
                pass
    return created

global _TARGET_REF
try:
    _TARGET_REF
except:
    _TARGET_REF = None

if repick or _TARGET_REF is None:
    try:
        _TARGET_REF = uidoc.Selection.PickObject(Selection.ObjectType.LinkedElement, "Pick linked DWG/Revit object")
    except:
        try:
            _TARGET_REF = uidoc.Selection.PickObject(Selection.ObjectType.Element, "Pick DWG/Revit object")
        except OperationCanceledException:
            OUT = []

if _TARGET_REF is not None:
    run = get_primary_top_run_from_pick(_TARGET_REF, tol)
    TransactionManager.Instance.EnsureInTransaction(doc)
    created = make_selectable_model_curves(run)
    TransactionManager.Instance.TransactionTaskDone()
    OUT = {
        "proxy_count": len(created),
        "run_curve_count": len(run),
        "elements": created
    }
elif 'OUT' not in globals():
    OUT = []
'@

$json.Name = 'civil3d_dwg_outer_top_single_run_extract_only_linked'
$out = $json | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($target, $out, [System.Text.Encoding]::UTF8)
(Get-Item -LiteralPath $target).LastWriteTime = Get-Date
Get-Item -LiteralPath $backup, $target | Select-Object Name, Length, LastWriteTime
