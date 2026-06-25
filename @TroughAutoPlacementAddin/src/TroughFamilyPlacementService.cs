using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TroughAutoPlacementAddin
{
    public static class TroughFamilyPlacementService
    {
        private const double FeetPerMm = 1.0 / 304.8;
        private const double MinCurveLength = FeetPerMm;

        public static IList<Curve> GetCurvesFromReference(Document document, Reference reference)
        {
            var curves = new List<Curve>();
            if (document == null || reference == null)
                return curves;

            curves.AddRange(GetCurvesFromLinkedReference(document, reference));
            if (curves.Count > 0)
                return curves;

            Element element = document.GetElement(reference.ElementId);
            if (element == null || element is RevitLinkInstance)
                return curves;

            GeometryObject geometryObject = null;
            try
            {
                geometryObject = element.GetGeometryObjectFromReference(reference);
            }
            catch
            {
                geometryObject = null;
            }

            curves.AddRange(GetCurvesFromGeometryObject(element, geometryObject));
            if (curves.Count > 0)
                return curves;

            if (reference.GlobalPoint != null)
                return GetClosestCurves(GetCurvesFromElement(element), reference.GlobalPoint, 1);

            return GetCurvesFromElement(element);
        }

        public static IList<ElementId> PlaceFamilies(
            Document document,
            IList<Curve> mainCurves,
            IList<Curve> sideCurves,
            FamilyPlacementSettings settings)
        {
            if (document == null)
                throw new InvalidOperationException("Revit document is not available.");
            if (settings == null || settings.FamilySymbolId == null || settings.FamilySymbolId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Select a family type.");
            if (mainCurves == null || mainCurves.Count == 0)
                throw new InvalidOperationException("Select Main edges.");
            if (sideCurves == null || sideCurves.Count == 0)
                throw new InvalidOperationException("Select Side edges.");

            FamilySymbol symbol = document.GetElement(settings.FamilySymbolId) as FamilySymbol;
            if (symbol == null)
                throw new InvalidOperationException("Selected family type was not found.");

            double spacing = settings.SpacingMm * FeetPerMm;
            double tolerance = Math.Max(0.0, settings.ToleranceMm * FeetPerMm);
            double perpendicularOffset = settings.PerpendicularOffsetMm * FeetPerMm;
            double parallelOffset = Math.Max(0.0, settings.ParallelOffsetMm * FeetPerMm);

            if (spacing <= MinCurveLength)
                throw new InvalidOperationException("Spacing must be greater than 1 mm.");

            IList<IList<Curve>> mainRuns = BuildConnectedRuns(mainCurves, tolerance);
            IList<IList<Curve>> sideRuns = BuildConnectedRuns(sideCurves, tolerance);
            IList<RunPair> pairs = MatchRuns(mainRuns, sideRuns);
            if (pairs.Count == 0)
                throw new InvalidOperationException("Could not pair Main edges with Side edges.");

            var createdIds = new List<ElementId>();
            using (var transaction = new Transaction(document, "\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58"))
            {
                transaction.Start();

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                foreach (RunPair pair in pairs)
                    PlaceFamilyRun(document, symbol, pair.MainRun, pair.SideRun, spacing, parallelOffset, perpendicularOffset, settings.RotationMode, createdIds);

                transaction.Commit();
            }

            return createdIds;
        }

        private static IList<Curve> GetCurvesFromLinkedReference(Document document, Reference reference)
        {
            var curves = new List<Curve>();
            if (reference.LinkedElementId == ElementId.InvalidElementId)
                return curves;

            RevitLinkInstance linkInstance = document.GetElement(reference.ElementId) as RevitLinkInstance;
            if (linkInstance == null)
                return curves;

            Document linkedDocument = linkInstance.GetLinkDocument();
            Element linkedElement = linkedDocument == null ? null : linkedDocument.GetElement(reference.LinkedElementId);
            if (linkedElement == null)
                return curves;

            Reference linkedReference = null;
            try
            {
                linkedReference = reference.CreateReferenceInLink();
            }
            catch
            {
                linkedReference = null;
            }

            GeometryObject geometryObject = null;
            try
            {
                geometryObject = linkedReference == null ? null : linkedElement.GetGeometryObjectFromReference(linkedReference);
            }
            catch
            {
                geometryObject = null;
            }

            Transform transform = linkInstance.GetTotalTransform() ?? linkInstance.GetTransform() ?? Transform.Identity;
            foreach (Curve curve in GetCurvesFromGeometryObject(linkedElement, geometryObject))
                AddCurve(curves, curve, transform);

            if (curves.Count == 0)
            {
                foreach (Curve curve in GetCurvesFromElement(linkedElement))
                    AddCurve(curves, curve, transform);
            }

            if (reference.GlobalPoint != null)
                return GetClosestCurves(curves, reference.GlobalPoint, 1);

            return curves;
        }

        private static IList<Curve> GetCurvesFromElement(Element element)
        {
            var curves = new List<Curve>();
            if (element == null)
                return curves;

            CurveElement curveElement = element as CurveElement;
            if (curveElement != null)
            {
                AddCurve(curves, curveElement.GeometryCurve, Transform.Identity);
                return curves;
            }

            LocationCurve locationCurve = element.Location as LocationCurve;
            if (locationCurve != null)
            {
                AddCurve(curves, locationCurve.Curve, Transform.Identity);
                return curves;
            }

            Options options = new Options();
            options.ComputeReferences = true;
            options.IncludeNonVisibleObjects = true;
            CollectCurvesFromGeometry(element.get_Geometry(options), GetElementTransform(element), curves);
            return curves;
        }

        private static IList<Curve> GetCurvesFromGeometryObject(Element element, GeometryObject geometryObject)
        {
            var curves = new List<Curve>();
            Transform transform = GetElementTransform(element);
            CollectCurveFromGeometryObject(geometryObject, transform, curves);
            return curves;
        }

        private static void CollectCurvesFromGeometry(GeometryElement geometryElement, Transform transform, IList<Curve> curves)
        {
            if (geometryElement == null)
                return;

            foreach (GeometryObject geometryObject in geometryElement)
                CollectCurveFromGeometryObject(geometryObject, transform, curves);
        }

        private static void CollectCurveFromGeometryObject(GeometryObject geometryObject, Transform transform, IList<Curve> curves)
        {
            if (geometryObject == null)
                return;

            Curve curve = geometryObject as Curve;
            if (curve != null)
            {
                AddCurve(curves, curve, transform);
                return;
            }

            Edge edge = geometryObject as Edge;
            if (edge != null)
            {
                AddCurve(curves, edge.AsCurve(), transform);
                return;
            }

            PolyLine polyLine = geometryObject as PolyLine;
            if (polyLine != null)
            {
                foreach (Curve polyLineCurve in GetCurvesFromPolyLine(polyLine))
                    AddCurve(curves, polyLineCurve, transform);
                return;
            }

            GeometryInstance geometryInstance = geometryObject as GeometryInstance;
            if (geometryInstance != null)
            {
                Transform instanceTransform = geometryInstance.Transform ?? Transform.Identity;
                Transform nestedTransform = transform == null ? instanceTransform : transform.Multiply(instanceTransform);
                CollectCurvesFromGeometry(geometryInstance.GetInstanceGeometry(), nestedTransform, curves);
                CollectCurvesFromGeometry(geometryInstance.GetSymbolGeometry(), nestedTransform, curves);
            }
        }

        private static IList<Curve> GetCurvesFromPolyLine(PolyLine polyLine)
        {
            var curves = new List<Curve>();
            if (polyLine == null)
                return curves;

            IList<XYZ> points = polyLine.GetCoordinates();
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) <= MinCurveLength)
                    continue;

                curves.Add(Line.CreateBound(points[i], points[i + 1]));
            }

            return curves;
        }

        private static void AddCurve(IList<Curve> curves, Curve curve, Transform transform)
        {
            if (curves == null || curve == null || curve.Length <= MinCurveLength)
                return;

            try
            {
                curves.Add(transform == null || transform.IsIdentity ? curve : curve.CreateTransformed(transform));
            }
            catch
            {
                curves.Add(curve);
            }
        }

        private static Transform GetElementTransform(Element element)
        {
            if (element == null)
                return Transform.Identity;

            ImportInstance importInstance = element as ImportInstance;
            if (importInstance != null)
                return importInstance.GetTotalTransform();

            Instance instance = element as Instance;
            if (instance != null)
                return instance.GetTransform();

            return Transform.Identity;
        }

        private static IList<Curve> GetClosestCurves(IList<Curve> curves, XYZ point, int count)
        {
            if (curves == null || curves.Count == 0 || point == null || count <= 0)
                return new List<Curve>();

            return curves
                .Where(curve => curve != null && curve.Length > MinCurveLength)
                .OrderBy(curve => DistanceToCurve(curve, point))
                .Take(count)
                .ToList();
        }

        private static double DistanceToCurve(Curve curve, XYZ point)
        {
            try
            {
                IntersectionResult result = curve.Project(point);
                if (result != null && result.XYZPoint != null)
                    return point.DistanceTo(result.XYZPoint);
            }
            catch
            {
            }

            return double.MaxValue;
        }

        private static void PlaceFamilyRun(
            Document document,
            FamilySymbol symbol,
            IList<Curve> mainRun,
            IList<Curve> sideRun,
            double spacing,
            double parallelOffset,
            double perpendicularOffset,
            int rotationMode,
            IList<ElementId> createdIds)
        {
            IList<double> lengths = mainRun.Select(curve => curve.Length).ToList();
            double totalLength = lengths.Sum();
            if (totalLength < MinCurveLength)
                return;

            var cumulative = new List<double>();
            double acc = 0.0;
            cumulative.Add(acc);
            foreach (double length in lengths)
            {
                acc += length;
                cumulative.Add(acc);
            }

            for (double distance = Math.Min(parallelOffset, totalLength); distance <= totalLength + 1e-9; distance += spacing)
            {
                CurveLocation location = LocateCurve(mainRun, cumulative, distance);
                if (location == null)
                    continue;

                XYZ targetPoint = location.Curve.Evaluate(location.Parameter, true);
                XYZ tangent = GetCurveTangent(location.Curve, location.Parameter);
                if (tangent == null)
                    continue;

                XYZ bestSidePoint = FindClosestProjectedPoint(sideRun, targetPoint);
                if (bestSidePoint == null)
                    continue;

                XYZ rawY = bestSidePoint - targetPoint;
                rawY = rawY - tangent.Multiply(rawY.DotProduct(tangent));
                if (rawY.GetLength() < 1e-6)
                    continue;

                XYZ yDirection = rawY.Normalize();
                XYZ zDirection = tangent.CrossProduct(yDirection);
                if (zDirection.GetLength() < 1e-9)
                    continue;

                zDirection = zDirection.Normalize();
                XYZ xDirection = yDirection.CrossProduct(zDirection);
                if (xDirection.GetLength() < 1e-9)
                    continue;

                xDirection = xDirection.Normalize();
                XYZ targetX = GetRotationTargetX(rotationMode, xDirection, yDirection);
                XYZ finalPoint = targetPoint + yDirection.Multiply(perpendicularOffset);

                FamilyInstance instance = CreateFamilyInstance(document, symbol, finalPoint);
                if (instance == null)
                    continue;

                document.Regenerate();
                RotateInstanceZOnly(document, instance, targetX);
                createdIds.Add(instance.Id);
            }
        }

        private static FamilyInstance CreateFamilyInstance(Document document, FamilySymbol symbol, XYZ finalPoint)
        {
            XYZ origin = XYZ.Zero;
            try
            {
                Plane plane = Plane.CreateByOriginAndBasis(origin, XYZ.BasisX, XYZ.BasisY);
                SketchPlane sketchPlane = SketchPlane.Create(document, plane);
                View view = document.ActiveView;
                if (view != null)
                    view.SketchPlane = sketchPlane;

                FamilyInstance instance = document.Create.NewFamilyInstance(origin, symbol, sketchPlane, StructuralType.NonStructural);
                MoveInstanceToPoint(document, instance, finalPoint);
                return instance;
            }
            catch
            {
                try
                {
                    return document.Create.NewFamilyInstance(finalPoint, symbol, StructuralType.NonStructural);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static void MoveInstanceToPoint(Document document, FamilyInstance instance, XYZ targetPoint)
        {
            LocationPoint locationPoint = instance == null ? null : instance.Location as LocationPoint;
            if (locationPoint == null)
                return;

            XYZ moveVector = targetPoint - locationPoint.Point;
            if (moveVector.GetLength() > 1e-9)
                ElementTransformUtils.MoveElement(document, instance.Id, moveVector);
        }

        private static IList<IList<Curve>> BuildConnectedRuns(IList<Curve> curves, double tolerance)
        {
            var segments = new List<RunCurve>();
            foreach (Curve curve in curves)
            {
                if (curve != null && curve.Length > MinCurveLength)
                    segments.Add(new RunCurve(curve));
            }

            var runs = new List<IList<Curve>>();
            foreach (RunCurve segment in segments)
            {
                if (segment.Used)
                    continue;

                var run = new List<RunCurve>();
                run.Add(segment);
                segment.Used = true;

                XYZ end = segment.End;
                while (true)
                {
                    RunCurve next;
                    int connectedEnd;
                    if (!TryFindConnectedCurve(segments, end, tolerance, out next, out connectedEnd))
                        break;

                    if (connectedEnd == 1)
                        next.Reverse();
                    run.Add(next);
                    next.Used = true;
                    end = next.End;
                }

                XYZ start = segment.Start;
                while (true)
                {
                    RunCurve next;
                    int connectedEnd;
                    if (!TryFindConnectedCurve(segments, start, tolerance, out next, out connectedEnd))
                        break;

                    if (connectedEnd == 0)
                        next.Reverse();
                    run.Insert(0, next);
                    next.Used = true;
                    start = next.Start;
                }

                runs.Add(run.Select(item => item.Curve).ToList());
            }

            return runs;
        }

        private static bool TryFindConnectedCurve(IList<RunCurve> segments, XYZ point, double tolerance, out RunCurve result, out int connectedEnd)
        {
            result = null;
            connectedEnd = -1;

            foreach (RunCurve segment in segments)
            {
                if (segment.Used)
                    continue;

                if (point.DistanceTo(segment.Start) <= tolerance)
                {
                    result = segment;
                    connectedEnd = 0;
                    return true;
                }

                if (point.DistanceTo(segment.End) <= tolerance)
                {
                    result = segment;
                    connectedEnd = 1;
                    return true;
                }
            }

            return false;
        }

        private static IList<RunPair> MatchRuns(IList<IList<Curve>> mainRuns, IList<IList<Curve>> sideRuns)
        {
            var pairs = new List<RunPair>();
            var usedSideIndexes = new HashSet<int>();

            foreach (IList<Curve> mainRun in mainRuns)
            {
                int bestIndex = -1;
                double bestDifference = double.MaxValue;
                double mainLength = mainRun.Sum(curve => curve.Length);

                for (int i = 0; i < sideRuns.Count; i++)
                {
                    if (usedSideIndexes.Contains(i))
                        continue;

                    double sideLength = sideRuns[i].Sum(curve => curve.Length);
                    double difference = Math.Abs(mainLength - sideLength);
                    if (difference < bestDifference)
                    {
                        bestDifference = difference;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    usedSideIndexes.Add(bestIndex);
                    pairs.Add(new RunPair(mainRun, sideRuns[bestIndex]));
                }
            }

            return pairs;
        }

        private static CurveLocation LocateCurve(IList<Curve> run, IList<double> cumulative, double distance)
        {
            for (int i = 0; i < run.Count; i++)
            {
                if (distance <= cumulative[i + 1] + 1e-9)
                {
                    double length = run[i].Length;
                    double parameter = length <= 1e-9 ? 0.0 : (distance - cumulative[i]) / length;
                    return new CurveLocation(run[i], Math.Max(0.0, Math.Min(1.0, parameter)));
                }
            }

            return run.Count == 0 ? null : new CurveLocation(run[run.Count - 1], 1.0);
        }

        private static XYZ GetCurveTangent(Curve curve, double normalizedParameter)
        {
            try
            {
                Transform derivatives = curve.ComputeDerivatives(normalizedParameter, true);
                XYZ tangent = derivatives == null ? null : derivatives.BasisX;
                return tangent == null || tangent.GetLength() < 1e-9 ? null : tangent.Normalize();
            }
            catch
            {
                IList<XYZ> points = curve.Tessellate();
                return GetPointListDirection(points);
            }
        }

        private static XYZ FindClosestProjectedPoint(IList<Curve> curves, XYZ targetPoint)
        {
            XYZ bestPoint = null;
            double bestDistance = double.MaxValue;

            foreach (Curve curve in curves)
            {
                IntersectionResult projection = null;
                try
                {
                    projection = curve.Project(targetPoint);
                }
                catch
                {
                    projection = null;
                }

                if (projection == null || projection.XYZPoint == null)
                    continue;

                double distance = projection.XYZPoint.DistanceTo(targetPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = projection.XYZPoint;
                }
            }

            return bestPoint;
        }

        private static XYZ GetRotationTargetX(int rotationMode, XYZ xDirection, XYZ yDirection)
        {
            if (rotationMode == 1)
                return yDirection;
            if (rotationMode == 2)
                return xDirection.Negate();

            return xDirection;
        }

        private static void RotateInstanceZOnly(Document document, FamilyInstance instance, XYZ targetX)
        {
            XYZ currentX = GetInstanceXAxis(instance);
            targetX = FlattenAndNormalize(targetX);
            if (currentX == null || targetX == null)
                return;

            double angle = SignedAngleXY(currentX, targetX);
            if (Math.Abs(angle) < 1e-6)
                return;

            LocationPoint locationPoint = instance.Location as LocationPoint;
            if (locationPoint == null)
                return;

            Line axis = Line.CreateBound(locationPoint.Point, locationPoint.Point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(document, instance.Id, axis, angle);
        }

        private static XYZ GetInstanceXAxis(FamilyInstance instance)
        {
            XYZ hand = FlattenAndNormalize(instance.HandOrientation);
            if (hand != null)
                return hand;

            XYZ facing = FlattenAndNormalize(instance.FacingOrientation);
            return facing ?? XYZ.BasisX;
        }

        private static XYZ GetPointListDirection(IList<XYZ> points)
        {
            if (points == null || points.Count < 2)
                return null;

            for (int i = 0; i < points.Count - 1; i++)
            {
                XYZ direction = FlattenAndNormalize(points[i + 1] - points[i]);
                if (direction != null)
                    return direction;
            }

            return null;
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
                return null;

            XYZ flat = new XYZ(vector.X, vector.Y, 0.0);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        private static double SignedAngleXY(XYZ from, XYZ to)
        {
            double dot = Math.Max(-1.0, Math.Min(1.0, from.DotProduct(to)));
            double angle = Math.Acos(dot);
            double crossZ = from.X * to.Y - from.Y * to.X;
            return crossZ < 0.0 ? -angle : angle;
        }

        private static Curve ReverseCurve(Curve curve)
        {
            try
            {
                return curve.CreateReversed();
            }
            catch
            {
                return curve;
            }
        }

        private class RunCurve
        {
            public RunCurve(Curve curve)
            {
                Curve = curve;
                Start = curve.GetEndPoint(0);
                End = curve.GetEndPoint(1);
            }

            public Curve Curve { get; private set; }
            public XYZ Start { get; private set; }
            public XYZ End { get; private set; }
            public bool Used { get; set; }

            public void Reverse()
            {
                Curve = ReverseCurve(Curve);
                Start = Curve.GetEndPoint(0);
                End = Curve.GetEndPoint(1);
            }
        }

        private class RunPair
        {
            public RunPair(IList<Curve> mainRun, IList<Curve> sideRun)
            {
                MainRun = mainRun;
                SideRun = sideRun;
            }

            public IList<Curve> MainRun { get; private set; }
            public IList<Curve> SideRun { get; private set; }
        }

        private class CurveLocation
        {
            public CurveLocation(Curve curve, double parameter)
            {
                Curve = curve;
                Parameter = parameter;
            }

            public Curve Curve { get; private set; }
            public double Parameter { get; private set; }
        }
    }
}
