$source = Get-ChildItem -LiteralPath "c:\Users\user\Desktop\codex" -Filter "*.dyn" | Where-Object { $_.Name -like "*260112*" -and $_.Name -notlike "backup_*" -and $_.Name -notlike "safe_*" -and $_.Name -notlike "aligned_*" -and $_.Name -notlike "origin_*" -and $_.Name -notlike "edge_fit_*" } | Select-Object -First 1
if ($null -eq $source) {
    throw "260112 source file not found."
}

$newPath = Join-Path $source.DirectoryName "edge_fit_260112_nrbscurve.dyn"
Copy-Item -LiteralPath $source.FullName -Destination $newPath -Force

$code = @'
# -*- coding: utf-8 -*-
import clr
import math

clr.AddReference("RevitServices")
clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")
clr.AddReference("System")

from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager
from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Structure import StructuralType
from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI import Selection
from System.Collections.Generic import List

doc   = DocumentManager.Instance.CurrentDBDocument
uidoc = DocumentManager.Instance.CurrentUIApplication.ActiveUIDocument
view  = doc.ActiveView

spacing_mm  = IN[0]
symbol_in   = IN[1]
tol_mm      = IN[2]
rotate_mode = IN[3]
repick      = IN[4]
off_perp_mm = IN[5]
off_par_mm  = IN[6]

spacing = float(spacing_mm) / 304.8
tol     = float(tol_mm) / 304.8
off_perp = float(off_perp_mm) / 304.8
off_par  = float(off_par_mm) / 304.8

symbols = UnwrapElement(symbol_in)
symbol  = symbols[0] if isinstance(symbols, list) else symbols

def dist(a, b): return a.DistanceTo(b)

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

def refs_to_curves(refs):
    cs = []
    for r in refs:
        e = doc.GetElement(r.ElementId)
        if e is None:
            continue
        g = e.GetGeometryObjectFromReference(r)
        if g is None:
            continue
        try:
            c = g.AsCurve()
        except:
            c = None
        if c is not None:
            cs.append(c)
    return cs

def build_runs(curves, tol):
    segs = []
    for i, c in enumerate(curves):
        segs.append({"id": i, "curve": c, "p0": c.GetEndPoint(0), "p1": c.GetEndPoint(1), "used": False})
    runs = []

    def find_conn(pt):
        for s in segs:
            if s["used"]:
                continue
            if dist(pt, s["p0"]) <= tol: return s, 0
            if dist(pt, s["p1"]) <= tol: return s, 1
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

def match_runs(main_runs, side_runs):
    pairs = []
    used = set()
    for mr in main_runs:
        best = None
        best_d = None
        for i, sr in enumerate(side_runs):
            if i in used:
                continue
            d = abs(sum([c.Length for c in mr]) - sum([c.Length for c in sr]))
            if best_d is None or d < best_d:
                best_d = d
                best = i
        if best is not None:
            used.add(best)
            pairs.append((mr, side_runs[best]))
    return pairs

def get_instance_point(inst):
    try:
        loc = inst.Location
        if hasattr(loc, "Point") and loc.Point is not None:
            return loc.Point
    except:
        pass
    try:
        box = inst.get_BoundingBox(view)
        if box is not None:
            return (box.Min + box.Max) * 0.5
    except:
        pass
    return XYZ.Zero

global _MAIN_REFS, _SIDE_REFS
try: _MAIN_REFS
except: _MAIN_REFS=None
try: _SIDE_REFS
except: _SIDE_REFS=None

try:
    uidoc.Selection.SetElementIds(List[ElementId]())
except:
    pass

if repick or _MAIN_REFS is None or _SIDE_REFS is None:
    try:
        main_refs = uidoc.Selection.PickObjects(
            Selection.ObjectType.Edge, "1) Main Edge 선택 (Shift)"
        )
        side_refs = uidoc.Selection.PickObjects(
            Selection.ObjectType.Edge, "2) Side Edge 선택 (Shift)"
        )
    except OperationCanceledException:
        OUT = []
    else:
        _MAIN_REFS = main_refs
        _SIDE_REFS = side_refs
else:
    main_refs = _MAIN_REFS
    side_refs = _SIDE_REFS

if 'main_refs' in globals() and 'side_refs' in globals() and symbol is not None and spacing > 0:
    main_runs = build_runs(refs_to_curves(main_refs), tol)
    side_runs = build_runs(refs_to_curves(side_refs), tol)
    pairs = match_runs(main_runs, side_runs)

    TransactionManager.Instance.EnsureInTransaction(doc)
    if not symbol.IsActive:
        symbol.Activate()

    instances = []
    origin_pt = XYZ.Zero

    for mr, sr in pairs:
        lens=[c.Length for c in mr]
        total=sum(lens)
        if total < 1e-6:
            continue

        cum=[0.0]
        acc=0.0
        for L in lens:
            acc+=L
            cum.append(acc)

        def locate(d):
            for i, L in enumerate(lens):
                if d <= cum[i+1] + 1e-9:
                    t = (d-cum[i])/L if L > 1e-9 else 0.0
                    return i, max(0.0, min(1.0, t))
            return len(lens)-1, 1.0

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

            best_pt=None
            best_d=None
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
            zDir = zDir.Normalize()
            xDir = yDir.CrossProduct(zDir).Normalize()

            yDir_fixed = yDir

            if rotate_mode == 1:
                xDir, yDir = yDir, xDir.Negate()
            elif rotate_mode == 2:
                xDir = xDir.Negate()
                yDir = yDir.Negate()

            target_pt = target_pt + (yDir_fixed * off_perp)

            plane = Plane.CreateByOriginAndBasis(origin_pt, xDir, yDir)
            skp = SketchPlane.Create(doc, plane)
            view.SketchPlane = skp

            inst = doc.Create.NewFamilyInstance(
                origin_pt, symbol, skp, StructuralType.NonStructural
            )

            current_pt = get_instance_point(inst)
            move_vec = target_pt - current_pt
            ElementTransformUtils.MoveElement(doc, inst.Id, move_vec)
            instances.append(inst)

            d += spacing

    TransactionManager.Instance.TransactionTaskDone()
    OUT = instances
elif 'OUT' not in globals():
    OUT = []
'@

$json = [System.IO.File]::ReadAllText($newPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$node = $json.Nodes | Where-Object { $_.ConcreteType -like "PythonNodeModels.PythonNode*" } | Select-Object -First 1
if ($null -eq $node) {
    throw "Python node not found in copied file."
}
$node.Code = $code
$json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $newPath -Encoding UTF8
