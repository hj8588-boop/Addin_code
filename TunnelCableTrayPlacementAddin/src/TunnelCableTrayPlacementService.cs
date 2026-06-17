using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace TunnelCableTrayPlacementAddin
{
    public static class TunnelCableTrayPlacementService
    {
        private const double FeetPerMm = 1.0 / 304.8;

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
            return curve ?? GetCurveFromElement(element);
        }

        public static XYZ GetCurveDirection(Curve curve)
        {
            if (curve == null)
                return null;

            return GetPointListDirection(curve.Tessellate());
        }

        public static int PlaceTrays(Document document, IList<Curve> centerlines, PlacementSettings settings, XYZ preferredDirection)
        {
            return PlaceTraysAndReturnIds(document, centerlines, settings, preferredDirection, "터널 케이블 트레이 자동배치").Count;
        }

        public static IList<ElementId> PreviewTrays(Document document, IList<Curve> centerlines, PlacementSettings settings, XYZ preferredDirection)
        {
            return PlaceTraysAndReturnIds(document, centerlines, settings, preferredDirection, "터널 케이블 트레이 자동배치 미리보기");
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
            string transactionName)
        {
            if (document == null)
                throw new InvalidOperationException("Revit 문서가 없습니다.");
            if (settings == null || settings.CableTrayTypeId == null || settings.CableTrayTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("케이블 트레이 타입을 선택하세요.");

            List<PathSegment> path = BuildPath(centerlines, preferredDirection);
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

                for (double distance = startDistance; distance < endDistance - 1e-9; distance += segmentLength)
                {
                    double nextDistance = Math.Min(endDistance, distance + segmentLength);
                    PathPoint start = EvaluatePath(path, distance);
                    PathPoint end = EvaluatePath(path, nextDistance);
                    XYZ startPoint = ApplyOffsetAndHeight(start.Point, start.Tangent, offset, heightOffset);
                    XYZ endPoint = ApplyOffsetAndHeight(end.Point, end.Tangent, offset, heightOffset);

                    if (startPoint.DistanceTo(endPoint) < 1e-6)
                        continue;

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

        private static Curve GetCurveFromLinkedReference(Document document, Reference reference)
        {
            if (reference.LinkedElementId == ElementId.InvalidElementId)
                return null;

            RevitLinkInstance linkInstance = document.GetElement(reference.ElementId) as RevitLinkInstance;
            if (linkInstance == null)
                return null;

            Document linkedDocument = linkInstance.GetLinkDocument();
            if (linkedDocument == null)
                return null;

            Element linkedElement = linkedDocument.GetElement(reference.LinkedElementId);
            Curve curve = GetCurveFromElement(linkedElement);
            if (curve == null)
                return null;

            Transform transform = linkInstance.GetTotalTransform() ?? linkInstance.GetTransform();
            return transform == null ? curve : curve.CreateTransformed(transform);
        }

        private static Curve GetCurveFromGeometryObject(GeometryObject geometryObject)
        {
            if (geometryObject == null)
                return null;

            Curve curve = geometryObject as Curve;
            if (curve != null)
                return curve;

            Edge edge = geometryObject as Edge;
            return edge == null ? null : edge.AsCurve();
        }

        private static List<PathSegment> BuildPath(IList<Curve> curves, XYZ preferredDirection)
        {
            var result = new List<PathSegment>();
            if (curves == null)
                return result;

            double cumulative = 0.0;
            foreach (Curve curve in curves)
            {
                if (curve == null)
                    continue;

                AddCurveToPath(result, curve, preferredDirection, ref cumulative);
            }

            return result;
        }

        private static void AddCurveToPath(List<PathSegment> result, Curve curve, XYZ preferredDirection, ref double cumulative)
        {
            var points = new List<XYZ>(curve.Tessellate());
            XYZ curveDirection = GetPointListDirection(points);
            XYZ preferredFlat = FlattenAndNormalize(preferredDirection);
            if (curveDirection != null && preferredFlat != null && curveDirection.DotProduct(preferredFlat) < 0.0)
                points.Reverse();

            for (int i = 0; i < points.Count - 1; i++)
            {
                XYZ start = points[i];
                XYZ end = points[i + 1];
                double length = start.DistanceTo(end);
                if (length < 1e-9)
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

        private static PathPoint EvaluatePath(List<PathSegment> path, double distance)
        {
            if (distance <= 0)
                return path[0].Evaluate(0.0);

            for (int i = 0; i < path.Count; i++)
            {
                PathSegment segment = path[i];
                if (distance <= segment.EndDistance + 1e-9)
                {
                    double segmentLength = segment.EndDistance - segment.StartDistance;
                    double t = segmentLength <= 1e-9
                        ? 0.0
                        : (distance - segment.StartDistance) / segmentLength;
                    return segment.Evaluate(Math.Max(0.0, Math.Min(1.0, t)));
                }
            }

            return path[path.Count - 1].Evaluate(1.0);
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
