using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace TunnelCableTrayPlacementAddin
{
    public static class TunnelCableTrayPlacementService
    {
        private const double FeetPerMm = 1.0 / 304.8;
        private const double MinCreatedCurveLength = FeetPerMm;

        public static Curve GetCurveFromElement(Element element)
        {
            if (element == null)
                return null;

            var curveElement = element as CurveElement;
            if (curveElement != null)
                return curveElement.GeometryCurve;

            LocationCurve locationCurve = element.Location as LocationCurve;
            return locationCurve == null ? null : locationCurve.Curve;
        }

        public static IList<Curve> GetCurvesFromElement(Element element)
        {
            var curves = new List<Curve>();
            Curve curve = GetCurveFromElement(element);
            if (curve != null)
            {
                curves.Add(curve);
                return curves;
            }

            Group group = element as Group;
            if (group != null)
            {
                Document document = group.Document;
                foreach (ElementId memberId in group.GetMemberIds())
                {
                    Element member = document.GetElement(memberId);
                    curves.AddRange(GetCurvesFromElement(member));
                }

                return curves;
            }

            IList<Curve> geometryCurves = GetCurvesFromGeometry(element);
            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null)
                curves.AddRange(GetLongestCurves(geometryCurves, 1));
            else
                curves.AddRange(geometryCurves);
            return curves;
        }

        public static Curve GetCurveFromReference(Document document, Reference reference)
        {
            if (document == null || reference == null)
                return null;

            Curve linkedCurve = GetCurveFromLinkedReference(document, reference);
            if (linkedCurve != null)
                return linkedCurve;

            Element element = document.GetElement(reference.ElementId);
            if (element == null || element is RevitLinkInstance)
                return null;

            GeometryObject geometryObject = null;
            try
            {
                geometryObject = element.GetGeometryObjectFromReference(reference);
            }
            catch
            {
                geometryObject = null;
            }

            Curve curve = GetCurveFromGeometryObject(geometryObject);
            if (curve != null)
                return TransformCurveToDocument(element, curve);

            return curve ?? GetCurveFromElement(element);
        }

        public static IList<Curve> GetCurvesFromReference(Document document, Reference reference)
        {
            var curves = new List<Curve>();
            curves.AddRange(GetCurvesFromLinkedReference(document, reference));
            if (curves.Count > 0)
                return curves;

            Element element = document == null || reference == null
                ? null
                : document.GetElement(reference.ElementId);
            ImportInstance importInstance = element as ImportInstance;
            FamilyInstance familyInstance = element as FamilyInstance;
            if ((importInstance != null || familyInstance != null) && reference.GlobalPoint != null)
            {
                IList<Curve> elementCurves = GetCurvesFromElement(element);
                curves.AddRange(GetClosestCurves(elementCurves, reference.GlobalPoint, 1));
                if (curves.Count > 0)
                    return curves;
            }

            Curve curve = GetCurveFromReference(document, reference);
            if (curve != null)
            {
                curves.Add(curve);
                return curves;
            }

            curves.AddRange(GetCurvesFromElement(element));
            return curves;
        }

        public static bool IsDwgReference(Document document, Reference reference)
        {
            if (document == null || reference == null)
                return false;

            return document.GetElement(reference.ElementId) is ImportInstance;
        }

        public static IList<Curve> CreateModelLinesFromCurves(Document document, IList<Curve> curves)
        {
            var modelLineCurves = new List<Curve>();
            if (document == null || curves == null || curves.Count == 0)
                return modelLineCurves;

            using (var transaction = new Transaction(document, "DWG 기준선 모델라인 생성"))
            {
                transaction.Start();

                foreach (Curve curve in curves)
                {
                    foreach (Curve modelCurveInput in GetModelLineInputCurves(curve))
                    {
                        try
                        {
                            SketchPlane sketchPlane = CreateSketchPlaneForCurve(document, modelCurveInput);
                            ModelCurve modelCurve = document.Create.NewModelCurve(modelCurveInput, sketchPlane);
                            if (modelCurve != null && modelCurve.GeometryCurve != null)
                                modelLineCurves.Add(modelCurve.GeometryCurve);
                        }
                        catch
                        {
                        }
                    }
                }

                transaction.Commit();
            }

            return modelLineCurves;
        }

        public static string DescribeReferences(Document document, IList<Reference> references)
        {
            if (document == null || references == null || references.Count == 0)
                return "선택 정보가 없습니다.";

            var builder = new StringBuilder();
            int index = 1;
            foreach (Reference reference in references)
            {
                Element element = reference == null ? null : document.GetElement(reference.ElementId);
                Category category = element == null ? null : element.Category;
                builder.Append(index).Append(". ");
                builder.Append(element == null ? "(요소 없음)" : element.GetType().Name);
                builder.Append(" / Id=");
                builder.Append(reference == null ? "null" : reference.ElementId.Value.ToString());
                if (reference != null && reference.LinkedElementId != ElementId.InvalidElementId)
                    builder.Append(" / LinkedId=").Append(reference.LinkedElementId.Value);
                builder.Append(" / Category=");
                builder.Append(category == null ? "(없음)" : category.Name);
                builder.AppendLine();
                index++;
            }

            return builder.ToString();
        }

        public static bool IsSelectableCurveReference(Document document, Reference reference)
        {
            if (document == null || reference == null)
                return false;

            Element element = document.GetElement(reference.ElementId);
            RevitLinkInstance linkInstance = element as RevitLinkInstance;
            if (linkInstance != null)
            {
                Document linkedDocument = linkInstance.GetLinkDocument();
                Element linkedElement = linkedDocument == null ? null : linkedDocument.GetElement(reference.LinkedElementId);
                return GetCurveFromElement(linkedElement) != null;
            }

            return GetCurvesFromElement(element).Count > 0;
        }

        public static XYZ GetCurveDirection(Curve curve)
        {
            if (curve == null)
                return null;

            return GetPointListDirection(curve.Tessellate());
        }

        public static int PlaceTrays(Document document, IList<Curve> centerlines, PlacementSettings settings, XYZ preferredDirection, XYZ preferredStartPoint)
        {
            return PlaceTraysAndReturnIds(document, centerlines, settings, preferredDirection, preferredStartPoint, "터널 케이블 트레이 자동배치").Count;
        }

        public static IList<ElementId> PreviewTrays(Document document, IList<Curve> centerlines, PlacementSettings settings, XYZ preferredDirection, XYZ preferredStartPoint)
        {
            return PlaceTraysAndReturnIds(document, centerlines, settings, preferredDirection, preferredStartPoint, "터널 케이블 트레이 자동배치 미리보기");
        }

        public static void DeletePreviewTrays(Document document, IList<ElementId> elementIds)
        {
            if (document == null || elementIds == null || elementIds.Count == 0)
                return;

            using (var transaction = new Transaction(document, "터널 케이블 트레이 미리보기 삭제"))
            {
                transaction.Start();
                foreach (ElementId id in elementIds.ToList())
                {
                    if (id != null && id != ElementId.InvalidElementId && document.GetElement(id) != null)
                        document.Delete(id);
                }
                transaction.Commit();
            }

            elementIds.Clear();
        }

        private static IList<ElementId> PlaceTraysAndReturnIds(
            Document document,
            IList<Curve> centerlines,
            PlacementSettings settings,
            XYZ preferredDirection,
            XYZ preferredStartPoint,
            string transactionName)
        {
            if (document == null)
                throw new InvalidOperationException("Revit 문서가 없습니다.");
            if (settings == null || settings.CableTrayTypeId == null || settings.CableTrayTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("케이블 트레이 타입을 선택하세요.");

            List<PathSegment> path = BuildPath(centerlines, preferredDirection, preferredStartPoint);
            if (path.Count == 0)
                throw new InvalidOperationException("선택한 기준선에서 유효한 경로를 만들 수 없습니다.");

            double totalLength = path[path.Count - 1].EndDistance;
            double startDistance = Math.Max(0.0, settings.StartDistanceMm * FeetPerMm);
            double endDistance = settings.EndDistanceMm <= 0.0
                ? totalLength
                : Math.Min(totalLength, settings.EndDistanceMm * FeetPerMm);
            double segmentLength = Math.Max(1.0 * FeetPerMm, settings.SegmentLengthMm * FeetPerMm);

            if (startDistance >= endDistance)
                throw new InvalidOperationException("시작 거리는 종료 거리보다 작아야 합니다.");

            Level level = GetBestLevel(document);
            if (level == null)
                throw new InvalidOperationException("케이블 트레이를 배치할 Level을 찾을 수 없습니다.");

            double offset = settings.OffsetMm * FeetPerMm;
            double heightOffset = settings.ElevationMm * FeetPerMm;
            double width = settings.WidthMm * FeetPerMm;
            double height = settings.HeightMm * FeetPerMm;

            var createdIds = new List<ElementId>();
            using (var transaction = new Transaction(document, transactionName))
            {
                transaction.Start();

                for (double distance = startDistance; distance < endDistance - 1e-9;)
                {
                    int segmentIndex = FindPathSegmentIndex(path, distance);
                    PathSegment segment = path[segmentIndex];
                    double nextDistance = Math.Min(Math.Min(endDistance, distance + segmentLength), segment.EndDistance);
                    PathPoint start = segment.EvaluateDistance(distance);
                    PathPoint end = segment.EvaluateDistance(nextDistance);
                    XYZ startPoint = ApplyOffsetAndHeight(start.Point, start.Tangent, offset, heightOffset);
                    XYZ endPoint = ApplyOffsetAndHeight(end.Point, end.Tangent, offset, heightOffset);

                    distance = nextDistance;

                    CableTray tray = CableTray.Create(document, settings.CableTrayTypeId, startPoint, endPoint, level.Id);
                    SetParameter(tray, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, width);
                    SetParameter(tray, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, height);
                    createdIds.Add(tray.Id);
                }

                transaction.Commit();
            }

            return createdIds;
        }

        private static void SetParameter(Element element, BuiltInParameter parameterId, double value)
        {
            Parameter parameter = element == null ? null : element.get_Parameter(parameterId);
            if (parameter != null && !parameter.IsReadOnly)
                parameter.Set(value);
        }

        private static XYZ ApplyOffsetAndHeight(XYZ point, XYZ tangent, double offset, double heightOffset)
        {
            XYZ flatTangent = FlattenAndNormalize(tangent) ?? XYZ.BasisX;
            XYZ side = new XYZ(-flatTangent.Y, flatTangent.X, 0.0);
            XYZ shifted = point + side.Multiply(offset);
            return shifted + XYZ.BasisZ.Multiply(heightOffset);
        }

        private static Level GetBestLevel(Document document)
        {
            ViewPlan viewPlan = document.ActiveView as ViewPlan;
            if (viewPlan != null && viewPlan.GenLevel != null)
                return viewPlan.GenLevel;

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation))
                .FirstOrDefault();
        }

        public static Curve GetCurveFromLinkedReference(Document document, Reference reference)
        {
            IList<Curve> curves = GetCurvesFromLinkedReference(document, reference);
            return curves.Count == 0 ? null : curves[0];
        }

        private static IList<Curve> GetCurvesFromLinkedReference(Document document, Reference reference)
        {
            var curves = new List<Curve>();
            if (document == null || reference == null || reference.LinkedElementId == ElementId.InvalidElementId)
                return curves;

            RevitLinkInstance linkInstance = document.GetElement(reference.ElementId) as RevitLinkInstance;
            if (linkInstance == null)
                return curves;

            Document linkedDocument = linkInstance.GetLinkDocument();
            if (linkedDocument == null)
                return curves;

            Element linkedElement = linkedDocument.GetElement(reference.LinkedElementId);
            Curve curve = GetLinkedGeometryCurve(linkedElement, reference) ?? GetCurveFromElement(linkedElement);
            Transform transform = linkInstance.GetTotalTransform() ?? linkInstance.GetTransform();
            if (curve != null)
            {
                curve = TransformCurveToDocument(linkedElement, curve);
                curves.Add(transform == null ? curve : curve.CreateTransformed(transform));
                return curves;
            }

            foreach (Curve linkedCurve in GetCurvesFromElement(linkedElement))
                curves.Add(transform == null ? linkedCurve : linkedCurve.CreateTransformed(transform));

            return curves;
        }

        private static Curve GetLinkedGeometryCurve(Element linkedElement, Reference hostReference)
        {
            if (linkedElement == null || hostReference == null)
                return null;

            try
            {
                Reference linkedReference = hostReference.CreateReferenceInLink();
                GeometryObject geometryObject = linkedElement.GetGeometryObjectFromReference(linkedReference);
                return GetCurveFromGeometryObject(geometryObject);
            }
            catch
            {
                return null;
            }
        }

        private static Curve TransformCurveToDocument(Element element, Curve curve)
        {
            Transform transform = GetElementTransform(element);
            if (curve == null || transform == null || transform.IsIdentity)
                return curve;

            return curve.CreateTransformed(transform);
        }

        private static Transform GetElementTransform(Element element)
        {
            if (element == null)
                return null;

            ImportInstance importInstance = element as ImportInstance;
            if (importInstance != null)
                return importInstance.GetTotalTransform();

            Instance instance = element as Instance;
            if (instance != null)
                return instance.GetTransform();

            return Transform.Identity;
        }

        private static Curve GetCurveFromGeometryObject(GeometryObject geometryObject)
        {
            if (geometryObject == null)
                return null;

            Curve curve = geometryObject as Curve;
            if (curve != null)
                return curve;

            Edge edge = geometryObject as Edge;
            if (edge != null)
                return edge.AsCurve();

            PolyLine polyLine = geometryObject as PolyLine;
            IList<Curve> polyLineCurves = GetCurvesFromPolyLine(polyLine);
            return polyLineCurves.Count == 0 ? null : polyLineCurves[0];
        }

        private static IList<Curve> GetCurvesFromGeometry(Element element)
        {
            var curves = new List<Curve>();
            if (element == null)
                return curves;

            Options options = new Options();
            options.ComputeReferences = true;
            options.IncludeNonVisibleObjects = true;
            GeometryElement geometryElement = element.get_Geometry(options);
            CollectCurvesFromGeometry(geometryElement, Transform.Identity, curves);
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
                curves.Add(transform == null || transform.IsIdentity ? curve : curve.CreateTransformed(transform));
                return;
            }

            Edge edge = geometryObject as Edge;
            if (edge != null)
            {
                Curve edgeCurve = edge.AsCurve();
                curves.Add(transform == null || transform.IsIdentity ? edgeCurve : edgeCurve.CreateTransformed(transform));
                return;
            }

            Solid solid = geometryObject as Solid;
            if (solid != null)
            {
                foreach (Edge solidEdge in solid.Edges)
                {
                    Curve edgeCurve = solidEdge.AsCurve();
                    if (edgeCurve != null && edgeCurve.Length > MinCreatedCurveLength)
                        curves.Add(transform == null || transform.IsIdentity ? edgeCurve : edgeCurve.CreateTransformed(transform));
                }
                return;
            }

            PolyLine polyLine = geometryObject as PolyLine;
            if (polyLine != null)
            {
                foreach (Curve polyLineCurve in GetCurvesFromPolyLine(polyLine))
                    curves.Add(transform == null || transform.IsIdentity ? polyLineCurve : polyLineCurve.CreateTransformed(transform));
                return;
            }

            GeometryInstance geometryInstance = geometryObject as GeometryInstance;
            if (geometryInstance != null)
            {
                Transform instanceTransform = geometryInstance.Transform ?? Transform.Identity;
                Transform nestedTransform = transform == null
                    ? instanceTransform
                    : transform.Multiply(instanceTransform);
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
                if (points[i].DistanceTo(points[i + 1]) <= MinCreatedCurveLength)
                    continue;

                try
                {
                    curves.Add(Line.CreateBound(points[i], points[i + 1]));
                }
                catch (Autodesk.Revit.Exceptions.ArgumentsInconsistentException)
                {
                }
            }

            return curves;
        }

        private static IList<Curve> GetModelLineInputCurves(Curve curve)
        {
            var curves = new List<Curve>();
            if (curve == null || curve.Length <= MinCreatedCurveLength)
                return curves;

            if (curve is Line || curve is Arc)
            {
                curves.Add(curve);
                return curves;
            }

            IList<XYZ> points = curve.Tessellate();
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) <= MinCreatedCurveLength)
                    continue;

                curves.Add(Line.CreateBound(points[i], points[i + 1]));
            }

            return curves;
        }

        private static SketchPlane CreateSketchPlaneForCurve(Document document, Curve curve)
        {
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ direction = (end - start).Normalize();
            XYZ normal = Math.Abs(start.Z - end.Z) < 1e-6
                ? XYZ.BasisZ
                : direction.CrossProduct(XYZ.BasisZ);

            if (normal.GetLength() < 1e-9)
                normal = direction.CrossProduct(XYZ.BasisX);
            if (normal.GetLength() < 1e-9)
                normal = XYZ.BasisZ;

            Plane plane = Plane.CreateByNormalAndOrigin(normal.Normalize(), start);
            return SketchPlane.Create(document, plane);
        }

        private static IList<Curve> GetLongestCurves(IList<Curve> curves, int count)
        {
            if (curves == null || curves.Count == 0 || count <= 0)
                return new List<Curve>();

            return curves
                .Where(curve => curve != null && curve.Length > MinCreatedCurveLength)
                .OrderByDescending(curve => curve.Length)
                .Take(count)
                .ToList();
        }

        private static IList<Curve> GetClosestCurves(IList<Curve> curves, XYZ point, int count)
        {
            if (curves == null || curves.Count == 0 || point == null || count <= 0)
                return new List<Curve>();

            return curves
                .Where(curve => curve != null && curve.Length > MinCreatedCurveLength)
                .OrderBy(curve => GetDistanceToCurve(curve, point))
                .Take(count)
                .ToList();
        }

        private static double GetDistanceToCurve(Curve curve, XYZ point)
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

        private static List<PathSegment> BuildPath(IList<Curve> curves, XYZ preferredDirection, XYZ preferredStartPoint)
        {
            var result = new List<PathSegment>();
            if (curves == null)
                return result;

            double cumulative = 0.0;
            foreach (CurvePath curvePath in OrderCurvePaths(curves, preferredDirection, preferredStartPoint))
            {
                AddCurvePathToPath(result, curvePath.Points, ref cumulative);
            }

            return result;
        }

        private static IList<CurvePath> OrderCurvePaths(IList<Curve> curves, XYZ preferredDirection, XYZ preferredStartPoint)
        {
            var remaining = new List<CurvePath>();
            XYZ preferredFlat = FlattenAndNormalize(preferredDirection);

            foreach (Curve curve in curves)
            {
                CurvePath curvePath = CreateCurvePath(curve, preferredFlat);
                if (curvePath != null)
                    remaining.Add(curvePath);
            }

            if (remaining.Count <= 1)
                return remaining;

            var ordered = new List<CurvePath>();
            int firstIndex = FindFirstCurvePathIndex(remaining, preferredFlat, preferredStartPoint);
            CurvePath current = remaining[firstIndex];
            remaining.RemoveAt(firstIndex);
            OrientFirstCurvePath(current, preferredFlat, preferredStartPoint);
            ordered.Add(current);

            while (remaining.Count > 0)
            {
                XYZ currentEnd = current.End;
                int bestIndex = 0;
                bool reverseBest = false;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < remaining.Count; i++)
                {
                    CurvePath candidate = remaining[i];
                    double startDistance = currentEnd.DistanceTo(candidate.Start);
                    if (startDistance < bestDistance)
                    {
                        bestDistance = startDistance;
                        bestIndex = i;
                        reverseBest = false;
                    }

                    double endDistance = currentEnd.DistanceTo(candidate.End);
                    if (endDistance < bestDistance)
                    {
                        bestDistance = endDistance;
                        bestIndex = i;
                        reverseBest = true;
                    }
                }

                current = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);
                if (reverseBest)
                    current.Reverse();
                ordered.Add(current);
            }

            return ordered;
        }

        private static CurvePath CreateCurvePath(Curve curve, XYZ preferredFlat)
        {
            if (curve == null)
                return null;

            var points = new List<XYZ>(curve.Tessellate());
            XYZ curveDirection = GetPointListDirection(points);
            if (curveDirection != null && preferredFlat != null && curveDirection.DotProduct(preferredFlat) < 0.0)
                points.Reverse();

            return points.Count < 2 ? null : new CurvePath(points);
        }

        private static int FindFirstCurvePathIndex(IList<CurvePath> paths, XYZ preferredFlat, XYZ preferredStartPoint)
        {
            if (paths == null || paths.Count == 0)
                return 0;

            int bestIndex = 0;

            if (preferredStartPoint != null)
            {
                double bestDistance = double.MaxValue;
                for (int i = 0; i < paths.Count; i++)
                {
                    double distance = Math.Min(
                        preferredStartPoint.DistanceTo(paths[i].Start),
                        preferredStartPoint.DistanceTo(paths[i].End));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }

            if (preferredFlat == null)
                return 0;

            double bestProjection = double.MaxValue;
            for (int i = 0; i < paths.Count; i++)
            {
                double projection = paths[i].Start.DotProduct(preferredFlat);
                if (projection < bestProjection)
                {
                    bestProjection = projection;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static void OrientFirstCurvePath(CurvePath path, XYZ preferredFlat, XYZ preferredStartPoint)
        {
            if (path == null)
                return;

            if (preferredStartPoint != null)
            {
                double startDistance = preferredStartPoint.DistanceTo(path.Start);
                double endDistance = preferredStartPoint.DistanceTo(path.End);
                if (endDistance < startDistance)
                    path.Reverse();
                return;
            }

            if (preferredFlat != null)
            {
                double startProjection = path.Start.DotProduct(preferredFlat);
                double endProjection = path.End.DotProduct(preferredFlat);
                if (endProjection < startProjection)
                    path.Reverse();
            }
        }

        private static void AddCurvePathToPath(List<PathSegment> result, IList<XYZ> points, ref double cumulative)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                XYZ start = points[i];
                XYZ end = points[i + 1];
                double length = start.DistanceTo(end);
                if (length < MinCreatedCurveLength)
                    continue;

                result.Add(new PathSegment(start, end, cumulative, cumulative + length));
                cumulative += length;
            }
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

        private static int FindPathSegmentIndex(List<PathSegment> path, double distance)
        {
            if (distance <= 0)
                return 0;

            for (int i = 0; i < path.Count; i++)
            {
                PathSegment segment = path[i];
                if (distance < segment.EndDistance - 1e-9)
                    return i;
            }

            return path.Count - 1;
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
                return null;

            XYZ flat = new XYZ(vector.X, vector.Y, 0.0);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        private class PathSegment
        {
            public PathSegment(XYZ start, XYZ end, double startDistance, double endDistance)
            {
                Start = start;
                End = end;
                StartDistance = startDistance;
                EndDistance = endDistance;
            }

            public XYZ Start { get; private set; }
            public XYZ End { get; private set; }
            public double StartDistance { get; private set; }
            public double EndDistance { get; private set; }

            public PathPoint Evaluate(double t)
            {
                XYZ point = Start + (End - Start).Multiply(t);
                XYZ tangent = (End - Start).Normalize();
                return new PathPoint(point, tangent);
            }

            public PathPoint EvaluateDistance(double distance)
            {
                double segmentLength = EndDistance - StartDistance;
                double t = segmentLength <= 1e-9
                    ? 0.0
                    : (distance - StartDistance) / segmentLength;
                return Evaluate(Math.Max(0.0, Math.Min(1.0, t)));
            }
        }

        private class CurvePath
        {
            public CurvePath(IList<XYZ> points)
            {
                Points = new List<XYZ>(points);
            }

            public List<XYZ> Points { get; private set; }
            public XYZ Start { get { return Points[0]; } }
            public XYZ End { get { return Points[Points.Count - 1]; } }

            public void Reverse()
            {
                Points.Reverse();
            }
        }

        private class PathPoint
        {
            public PathPoint(XYZ point, XYZ tangent)
            {
                Point = point;
                Tangent = tangent;
            }

            public XYZ Point { get; private set; }
            public XYZ Tangent { get; private set; }
        }


    }
}
