using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TunnelCableTrayPlacementAddin
{
    internal static class ConnectedCurveTracer
    {
        private const double FeetPerMm = 1.0 / 304.8;
        private const double MinimumBridgeLength = FeetPerMm;
        private static readonly double MinimumContinuationScore = Math.Cos(60.0 * Math.PI / 180.0);

        public static int LastCandidateCount { get; private set; }
        public static int LastConnectedCount { get; private set; }
        public static double LastPathLengthMm { get; private set; }

        public static IList<Curve> Trace(Document document, Reference reference, double toleranceMm)
        {
            var candidates = CollectCandidates(document, reference);
            LastCandidateCount = candidates.Count;
            LastConnectedCount = 0;
            LastPathLengthMm = 0.0;
            if (candidates.Count == 0)
                return candidates;

            double tolerance = Math.Max(1.0, toleranceMm) * FeetPerMm;
            IList<Curve> path = BuildConnectedPath(candidates, tolerance);
            if (path.Count == 1 && candidates.Count > 1)
                path = BuildConnectedPath(candidates, 200.0 * FeetPerMm);

            path = OrientFromPickedPoint(path, reference.GlobalPoint);
            LastConnectedCount = path.Count;
            foreach (Curve curve in path)
                LastPathLengthMm += curve.Length / FeetPerMm;
            return path;
        }

        private static List<Curve> CollectCandidates(Document document, Reference reference)
        {
            var result = new List<Curve>();
            if (document == null || reference == null)
                return result;

            Curve seedCurve = TunnelCableTrayPlacementService.GetCurveFromReference(document, reference);
            if (seedCurve == null)
                return result;

            Element hostElement = document.GetElement(reference.ElementId);
            RevitLinkInstance linkInstance = hostElement as RevitLinkInstance;
            if (linkInstance != null && reference.LinkedElementId != ElementId.InvalidElementId)
            {
                Document linkedDocument = linkInstance.GetLinkDocument();
                Element linkedElement = linkedDocument == null
                    ? null
                    : linkedDocument.GetElement(reference.LinkedElementId);
                Transform transform = linkInstance.GetTotalTransform()
                    ?? linkInstance.GetTransform()
                    ?? Transform.Identity;

                if (linkedElement is ImportInstance)
                {
                    var linkedCurves = new List<Curve>();
                    CollectImportCurves(
                        linkedDocument,
                        linkedElement as ImportInstance,
                        ElementId.InvalidElementId,
                        linkedCurves);
                    AddTransformedCurves(
                        linkedCurves,
                        transform,
                        result);
                }
                else if (linkedElement is CurveElement)
                {
                    CollectCurveElements(linkedDocument, null, transform, result);
                }
                else
                {
                    AddTransformedCurves(
                        TunnelCableTrayPlacementService.GetCurvesFromElement(linkedElement),
                        transform,
                        result);
                }
            }
            else if (hostElement is ImportInstance)
            {
                CollectImportCurves(
                    document,
                    hostElement as ImportInstance,
                    ElementId.InvalidElementId,
                    result);
            }
            else if (hostElement is CurveElement)
            {
                CollectCurveElements(document, document.ActiveView, Transform.Identity, result);
            }
            else if (hostElement != null && hostElement.Location is LocationCurve && hostElement.Category != null)
            {
                CollectLocationCurves(document, document.ActiveView, hostElement.Category.Id, result);
            }
            else
            {
                AddCurves(TunnelCableTrayPlacementService.GetCurvesFromElement(hostElement), result);
            }

            if (result.Count == 0)
                result.Add(seedCurve);

            result = RemoveDuplicateLines(result, 1.0 * FeetPerMm);
            return PutNearestFirst(result, reference.GlobalPoint);
        }

        private static ElementId GetReferenceGraphicsStyleId(Element element, Reference reference)
        {
            try
            {
                GeometryObject geometryObject = element.GetGeometryObjectFromReference(reference);
                return geometryObject == null
                    ? ElementId.InvalidElementId
                    : geometryObject.GraphicsStyleId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        private static void CollectImportCurves(
            Document document,
            ImportInstance importInstance,
            ElementId graphicsStyleId,
            IList<Curve> result)
        {
            if (importInstance == null)
                return;

            var options = new Options();
            options.ComputeReferences = false;
            options.IncludeNonVisibleObjects = false;
            if (document != null && document.ActiveView != null)
                options.View = document.ActiveView;

            GeometryElement geometry = importInstance.get_Geometry(options);
            CollectGeometryCurves(geometry, Transform.Identity, graphicsStyleId, result);
        }

        private static void CollectGeometryCurves(
            GeometryElement geometry,
            Transform transform,
            ElementId graphicsStyleId,
            IList<Curve> result)
        {
            if (geometry == null)
                return;

            foreach (GeometryObject geometryObject in geometry)
            {
                GeometryInstance geometryInstance = geometryObject as GeometryInstance;
                if (geometryInstance != null)
                {
                    CollectGeometryCurves(
                        geometryInstance.GetSymbolGeometry(),
                        transform.Multiply(geometryInstance.Transform),
                        graphicsStyleId,
                        result);
                    continue;
                }

                if (graphicsStyleId != ElementId.InvalidElementId
                    && geometryObject.GraphicsStyleId != graphicsStyleId)
                    continue;

                Curve curve = geometryObject as Curve;
                if (curve != null)
                {
                    Curve transformed = curve.CreateTransformed(transform);
                    if (IsUsable(transformed))
                        result.Add(transformed);
                    continue;
                }

                PolyLine polyLine = geometryObject as PolyLine;
                if (polyLine != null)
                {
                    IList<XYZ> points = polyLine.GetCoordinates();
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        XYZ start = transform.OfPoint(points[i]);
                        XYZ end = transform.OfPoint(points[i + 1]);
                        if (start.DistanceTo(end) > 1e-9)
                            result.Add(Line.CreateBound(start, end));
                    }
                    continue;
                }

                Solid solid = geometryObject as Solid;
                if (solid == null || solid.Edges == null)
                    continue;

                foreach (Edge edge in solid.Edges)
                {
                    Curve edgeCurve = edge.AsCurve();
                    if (edgeCurve == null)
                        continue;

                    Curve transformed = edgeCurve.CreateTransformed(transform);
                    if (IsUsable(transformed))
                        result.Add(transformed);
                }
            }
        }

        private static void CollectCurveElements(
            Document document,
            View view,
            Transform transform,
            IList<Curve> result)
        {
            if (document == null)
                return;

            FilteredElementCollector collector = view == null
                ? new FilteredElementCollector(document)
                : new FilteredElementCollector(document, view.Id);

            foreach (CurveElement element in collector.OfClass(typeof(CurveElement)))
            {
                Curve curve = element.GeometryCurve;
                if (IsUsable(curve))
                    result.Add(transform == null || transform.IsIdentity
                        ? curve
                        : curve.CreateTransformed(transform));
            }
        }

        private static void CollectLocationCurves(
            Document document,
            View view,
            ElementId categoryId,
            IList<Curve> result)
        {
            FilteredElementCollector collector = view == null
                ? new FilteredElementCollector(document)
                : new FilteredElementCollector(document, view.Id);
            collector.OfCategoryId(categoryId).WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                LocationCurve location = element.Location as LocationCurve;
                if (location != null && IsUsable(location.Curve))
                    result.Add(location.Curve);
            }
        }

        private static void AddCurves(IList<Curve> curves, IList<Curve> result)
        {
            if (curves == null)
                return;

            foreach (Curve curve in curves)
            {
                if (IsUsable(curve))
                    result.Add(curve);
            }
        }

        private static void AddTransformedCurves(
            IList<Curve> curves,
            Transform transform,
            IList<Curve> result)
        {
            if (curves == null)
                return;

            foreach (Curve curve in curves)
            {
                if (!IsUsable(curve))
                    continue;

                result.Add(transform == null || transform.IsIdentity
                    ? curve
                    : curve.CreateTransformed(transform));
            }
        }

        private static List<Curve> RemoveDuplicateLines(IList<Curve> curves, double tolerance)
        {
            var result = new List<Curve>();
            var lineKeys = new HashSet<LineKey>();

            foreach (Curve curve in curves)
            {
                if (!(curve is Line))
                {
                    result.Add(curve);
                    continue;
                }

                var key = new LineKey(
                    curve.GetEndPoint(0),
                    curve.GetEndPoint(1),
                    tolerance);
                if (lineKeys.Add(key))
                    result.Add(curve);
            }

            return result;
        }

        private static bool IsUsable(Curve curve)
        {
            return curve != null && curve.IsBound && curve.Length > 1e-9;
        }

        private static List<Curve> PutNearestFirst(IList<Curve> curves, XYZ pickedPoint)
        {
            var result = new List<Curve>();
            if (curves == null || curves.Count == 0)
                return result;

            int nearestIndex = 0;
            double nearestDistance = double.MaxValue;
            for (int i = 0; i < curves.Count; i++)
            {
                double distance = DistanceToCurve(curves[i], pickedPoint);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            result.Add(curves[nearestIndex]);
            for (int i = 0; i < curves.Count; i++)
            {
                if (i != nearestIndex)
                    result.Add(curves[i]);
            }
            return result;
        }

        private static double DistanceToCurve(Curve curve, XYZ point)
        {
            if (curve == null || point == null)
                return double.MaxValue;

            try
            {
                IntersectionResult projection = curve.Project(point);
                if (projection != null && projection.XYZPoint != null)
                    return point.DistanceTo(projection.XYZPoint);
            }
            catch
            {
            }

            return Math.Min(
                point.DistanceTo(curve.GetEndPoint(0)),
                point.DistanceTo(curve.GetEndPoint(1)));
        }

        private static IList<Curve> BuildConnectedPath(IList<Curve> curves, double tolerance)
        {
            if (curves == null || curves.Count == 0)
                return new List<Curve>();

            Dictionary<EndpointKey, List<EndpointCandidate>> endpointIndex =
                BuildEndpointIndex(curves, tolerance);
            var usedIndexes = new HashSet<int>();
            usedIndexes.Add(0);

            Curve seed = curves[0];
            XYZ seedStart = seed.GetEndPoint(0);
            XYZ seedEnd = seed.GetEndPoint(1);
            XYZ seedDirection = Normalize(seedEnd - seedStart);
            if (seedDirection == null)
                return new List<Curve> { seed };

            List<Curve> backward = TraceDirection(
                curves,
                endpointIndex,
                usedIndexes,
                seedStart,
                seedDirection.Negate(),
                tolerance);
            List<Curve> forward = TraceDirection(
                curves,
                endpointIndex,
                usedIndexes,
                seedEnd,
                seedDirection,
                tolerance);

            var ordered = new List<Curve>();
            for (int i = backward.Count - 1; i >= 0; i--)
                ordered.Add(backward[i].CreateReversed());
            ordered.Add(seed);
            ordered.AddRange(forward);
            return ordered;
        }

        private static Dictionary<EndpointKey, List<EndpointCandidate>> BuildEndpointIndex(
            IList<Curve> curves,
            double cellSize)
        {
            var result = new Dictionary<EndpointKey, List<EndpointCandidate>>();
            for (int i = 0; i < curves.Count; i++)
            {
                AddEndpoint(result, curves[i].GetEndPoint(0), cellSize, new EndpointCandidate(i, true));
                AddEndpoint(result, curves[i].GetEndPoint(1), cellSize, new EndpointCandidate(i, false));
            }
            return result;
        }

        private static void AddEndpoint(
            Dictionary<EndpointKey, List<EndpointCandidate>> index,
            XYZ point,
            double cellSize,
            EndpointCandidate candidate)
        {
            EndpointKey key = EndpointKey.FromPoint(point, cellSize);
            List<EndpointCandidate> entries;
            if (!index.TryGetValue(key, out entries))
            {
                entries = new List<EndpointCandidate>();
                index.Add(key, entries);
            }
            entries.Add(candidate);
        }

        private static List<Curve> TraceDirection(
            IList<Curve> curves,
            Dictionary<EndpointKey, List<EndpointCandidate>> endpointIndex,
            HashSet<int> usedIndexes,
            XYZ startPoint,
            XYZ incomingDirection,
            double tolerance)
        {
            var result = new List<Curve>();
            XYZ currentPoint = startPoint;
            XYZ currentDirection = incomingDirection;

            while (true)
            {
                EndpointCandidate best = FindEndpointCandidate(
                    curves,
                    endpointIndex,
                    usedIndexes,
                    currentPoint,
                    currentDirection,
                    tolerance);

                if (best != null)
                {
                    Curve selected = curves[best.CurveIndex];
                    Curve oriented = best.AtStart ? selected : selected.CreateReversed();
                    usedIndexes.Add(best.CurveIndex);
                    AddBridgeIfNeeded(result, currentPoint, oriented.GetEndPoint(0));
                    result.Add(oriented);
                    currentPoint = oriented.GetEndPoint(1);
                    currentDirection = Normalize(currentPoint - oriented.GetEndPoint(0)) ?? currentDirection;
                    continue;
                }

                int fallbackIndex;
                Curve fallback = FindInteriorConnection(
                    curves,
                    usedIndexes,
                    currentPoint,
                    currentDirection,
                    tolerance,
                    out fallbackIndex);
                if (fallback == null)
                {
                    fallback = FindCollinearExtension(
                        curves,
                        usedIndexes,
                        currentPoint,
                        currentDirection,
                        out fallbackIndex);
                }
                if (fallback == null)
                    break;

                usedIndexes.Add(fallbackIndex);
                AddBridgeIfNeeded(result, currentPoint, fallback.GetEndPoint(0));
                result.Add(fallback);
                currentPoint = fallback.GetEndPoint(1);
                currentDirection = Normalize(currentPoint - fallback.GetEndPoint(0)) ?? currentDirection;
            }

            return result;
        }

        private static void AddBridgeIfNeeded(IList<Curve> result, XYZ start, XYZ end)
        {
            if (result == null || start == null || end == null)
                return;

            if (start.DistanceTo(end) > MinimumBridgeLength)
                result.Add(Line.CreateBound(start, end));
        }

        private static EndpointCandidate FindEndpointCandidate(
            IList<Curve> curves,
            Dictionary<EndpointKey, List<EndpointCandidate>> endpointIndex,
            HashSet<int> usedIndexes,
            XYZ currentPoint,
            XYZ currentDirection,
            double tolerance)
        {
            EndpointCandidate best = null;
            double bestScore = double.MinValue;
            double bestDistance = double.MaxValue;
            EndpointKey center = EndpointKey.FromPoint(currentPoint, tolerance);

            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                List<EndpointCandidate> entries;
                var key = new EndpointKey(center.X + x, center.Y + y, center.Z + z);
                if (!endpointIndex.TryGetValue(key, out entries))
                    continue;

                foreach (EndpointCandidate entry in entries)
                {
                    if (usedIndexes.Contains(entry.CurveIndex))
                        continue;

                    Curve curve = curves[entry.CurveIndex];
                    XYZ joint = curve.GetEndPoint(entry.AtStart ? 0 : 1);
                    double distance = currentPoint.DistanceTo(joint);
                    if (distance > tolerance)
                        continue;

                    XYZ other = curve.GetEndPoint(entry.AtStart ? 1 : 0);
                    XYZ outgoing = Normalize(other - joint);
                    if (outgoing == null)
                        continue;

                    double score = currentDirection.DotProduct(outgoing);
                    if (score < MinimumContinuationScore)
                        continue;

                    if (score > bestScore + 1e-9
                        || (Math.Abs(score - bestScore) <= 1e-9 && distance < bestDistance))
                    {
                        best = entry;
                        bestScore = score;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        private static Curve FindCollinearExtension(
            IList<Curve> curves,
            HashSet<int> usedIndexes,
            XYZ currentPoint,
            XYZ currentDirection,
            out int curveIndex)
        {
            curveIndex = -1;
            Curve best = null;
            double bestGap = double.MaxValue;
            double bestScore = double.MinValue;
            double maximumGap = 10000000.0 * FeetPerMm;
            double maximumLateralGap = 100.0 * FeetPerMm;
            double minimumScore = Math.Cos(15.0 * Math.PI / 180.0);

            for (int i = 0; i < curves.Count; i++)
            {
                if (usedIndexes.Contains(i))
                    continue;

                Curve curve = curves[i];
                for (int jointEnd = 0; jointEnd <= 1; jointEnd++)
                {
                    XYZ joint = curve.GetEndPoint(jointEnd);
                    XYZ gap = joint - currentPoint;
                    double gapDistance = gap.GetLength();
                    if (gapDistance < 1e-9 || gapDistance > maximumGap)
                        continue;

                    double forwardDistance = gap.DotProduct(currentDirection);
                    if (forwardDistance <= 0.0)
                        continue;

                    XYZ lateral = gap - currentDirection.Multiply(forwardDistance);
                    if (lateral.GetLength() > maximumLateralGap)
                        continue;

                    XYZ other = curve.GetEndPoint(jointEnd == 0 ? 1 : 0);
                    XYZ outgoing = Normalize(other - joint);
                    if (outgoing == null)
                        continue;

                    double score = currentDirection.DotProduct(outgoing);
                    if (score < minimumScore)
                        continue;

                    if (gapDistance < bestGap - 1e-9
                        || (Math.Abs(gapDistance - bestGap) <= 1e-9 && score > bestScore))
                    {
                        best = jointEnd == 0 ? curve : curve.CreateReversed();
                        curveIndex = i;
                        bestGap = gapDistance;
                        bestScore = score;
                    }
                }
            }
            return best;
        }

        private static Curve FindInteriorConnection(
            IList<Curve> curves,
            HashSet<int> usedIndexes,
            XYZ currentPoint,
            XYZ currentDirection,
            double tolerance,
            out int curveIndex)
        {
            curveIndex = -1;
            Curve best = null;
            double bestScore = double.MinValue;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < curves.Count; i++)
            {
                if (usedIndexes.Contains(i))
                    continue;

                IntersectionResult projection;
                try
                {
                    projection = curves[i].Project(currentPoint);
                }
                catch
                {
                    continue;
                }
                if (projection == null)
                    continue;

                double distance = projection.XYZPoint.DistanceTo(currentPoint);
                if (distance > tolerance)
                    continue;

                for (int targetEnd = 0; targetEnd <= 1; targetEnd++)
                {
                    XYZ target = curves[i].GetEndPoint(targetEnd);
                    XYZ outgoing = Normalize(target - projection.XYZPoint);
                    if (outgoing == null)
                        continue;

                    double score = currentDirection.DotProduct(outgoing);
                    if (score < MinimumContinuationScore)
                        continue;

                    if (score < bestScore - 1e-9
                        || (Math.Abs(score - bestScore) <= 1e-9 && distance >= bestDistance))
                        continue;

                    Curve portion = CreateCurvePortion(curves[i], projection, targetEnd);
                    if (portion == null)
                        continue;

                    best = portion;
                    curveIndex = i;
                    bestScore = score;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static Curve CreateCurvePortion(Curve source, IntersectionResult projection, int targetEnd)
        {
            XYZ projectedPoint = projection.XYZPoint;
            XYZ targetPoint = source.GetEndPoint(targetEnd);
            if (projectedPoint.DistanceTo(targetPoint) < 1e-9)
                return null;

            if (source is Line)
                return Line.CreateBound(projectedPoint, targetPoint);

            try
            {
                Curve portion = source.Clone();
                double projectedParameter = projection.Parameter;
                double targetParameter = source.GetEndParameter(targetEnd);
                if (targetEnd == 0)
                {
                    portion.MakeBound(targetParameter, projectedParameter);
                    return portion.CreateReversed();
                }

                portion.MakeBound(projectedParameter, targetParameter);
                return portion;
            }
            catch
            {
                return Line.CreateBound(projectedPoint, targetPoint);
            }
        }

        private static IList<Curve> OrientFromPickedPoint(IList<Curve> path, XYZ pickedPoint)
        {
            if (path == null || path.Count == 0 || pickedPoint == null)
                return path ?? new List<Curve>();

            XYZ first = path[0].GetEndPoint(0);
            XYZ last = path[path.Count - 1].GetEndPoint(1);
            if (first.DistanceTo(pickedPoint) <= last.DistanceTo(pickedPoint))
                return path;

            var reversed = new List<Curve>();
            for (int i = path.Count - 1; i >= 0; i--)
                reversed.Add(path[i].CreateReversed());
            return reversed;
        }

        private static XYZ Normalize(XYZ vector)
        {
            return vector == null || vector.GetLength() < 1e-9
                ? null
                : vector.Normalize();
        }

        private struct LineKey : IEquatable<LineKey>
        {
            private readonly long _x1;
            private readonly long _y1;
            private readonly long _z1;
            private readonly long _x2;
            private readonly long _y2;
            private readonly long _z2;

            public LineKey(XYZ first, XYZ second, double tolerance)
            {
                long firstX = Quantize(first.X, tolerance);
                long firstY = Quantize(first.Y, tolerance);
                long firstZ = Quantize(first.Z, tolerance);
                long secondX = Quantize(second.X, tolerance);
                long secondY = Quantize(second.Y, tolerance);
                long secondZ = Quantize(second.Z, tolerance);

                bool reverse = IsAfter(
                    firstX,
                    firstY,
                    firstZ,
                    secondX,
                    secondY,
                    secondZ);

                _x1 = reverse ? secondX : firstX;
                _y1 = reverse ? secondY : firstY;
                _z1 = reverse ? secondZ : firstZ;
                _x2 = reverse ? firstX : secondX;
                _y2 = reverse ? firstY : secondY;
                _z2 = reverse ? firstZ : secondZ;
            }

            private static long Quantize(double value, double tolerance)
            {
                return (long)Math.Round(value / tolerance);
            }

            private static bool IsAfter(
                long x1,
                long y1,
                long z1,
                long x2,
                long y2,
                long z2)
            {
                if (x1 != x2)
                    return x1 > x2;
                if (y1 != y2)
                    return y1 > y2;
                return z1 > z2;
            }

            public bool Equals(LineKey other)
            {
                return _x1 == other._x1
                    && _y1 == other._y1
                    && _z1 == other._z1
                    && _x2 == other._x2
                    && _y2 == other._y2
                    && _z2 == other._z2;
            }

            public override bool Equals(object obj)
            {
                return obj is LineKey && Equals((LineKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x1.GetHashCode();
                    hash = (hash * 397) ^ _y1.GetHashCode();
                    hash = (hash * 397) ^ _z1.GetHashCode();
                    hash = (hash * 397) ^ _x2.GetHashCode();
                    hash = (hash * 397) ^ _y2.GetHashCode();
                    hash = (hash * 397) ^ _z2.GetHashCode();
                    return hash;
                }
            }
        }

        private struct EndpointKey : IEquatable<EndpointKey>
        {
            public EndpointKey(long x, long y, long z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public readonly long X;
            public readonly long Y;
            public readonly long Z;

            public static EndpointKey FromPoint(XYZ point, double cellSize)
            {
                return new EndpointKey(
                    (long)Math.Floor(point.X / cellSize),
                    (long)Math.Floor(point.Y / cellSize),
                    (long)Math.Floor(point.Z / cellSize));
            }

            public bool Equals(EndpointKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is EndpointKey && Equals((EndpointKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X.GetHashCode();
                    hash = (hash * 397) ^ Y.GetHashCode();
                    hash = (hash * 397) ^ Z.GetHashCode();
                    return hash;
                }
            }
        }

        private class EndpointCandidate
        {
            public EndpointCandidate(int curveIndex, bool atStart)
            {
                CurveIndex = curveIndex;
                AtStart = atStart;
            }

            public int CurveIndex { get; private set; }
            public bool AtStart { get; private set; }
        }
    }
}
