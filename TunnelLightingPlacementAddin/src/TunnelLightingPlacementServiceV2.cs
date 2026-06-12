using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TunnelLightingPlacementAddin
{
    public static class TunnelLightingPlacementServiceV2
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
            if (element == null)
                return null;
            if (element is RevitLinkInstance)
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

            Curve curve = GetCurveFromGeometryObject(geometryObject, reference.GlobalPoint);
            curve = TransformCurveIfCloser(element, curve, reference.GlobalPoint);
            curve = AlignCurveNearPickedPoint(curve, reference.GlobalPoint);
            return curve ?? GetCurveFromElement(element);
        }

        public static string GetReferenceDebugInfo(Document document, Reference reference)
        {
            var text = new StringBuilder();
            text.AppendLine("Host ElementId: " + (reference == null ? "(null)" : reference.ElementId.ToString()));
            text.AppendLine("LinkedElementId: " + (reference == null ? "(null)" : reference.LinkedElementId.ToString()));
            text.AppendLine("GlobalPoint: " + FormatPoint(reference == null ? null : reference.GlobalPoint));

            Element hostElement = document == null || reference == null ? null : document.GetElement(reference.ElementId);
            text.AppendLine("Host Type: " + (hostElement == null ? "(null)" : hostElement.GetType().FullName));
            text.AppendLine("Host Category: " + GetCategoryName(hostElement));

            RevitLinkInstance linkInstance = hostElement as RevitLinkInstance;
            if (linkInstance != null && reference.LinkedElementId != ElementId.InvalidElementId)
            {
                Document linkedDocument = linkInstance.GetLinkDocument();
                Element linkedElement = linkedDocument == null ? null : linkedDocument.GetElement(reference.LinkedElementId);
                text.AppendLine("Linked Doc: " + (linkedDocument == null ? "(null)" : linkedDocument.Title));
                text.AppendLine("Linked Type: " + (linkedElement == null ? "(null)" : linkedElement.GetType().FullName));
                text.AppendLine("Linked Category: " + GetCategoryName(linkedElement));
                text.AppendLine("Linked Name: " + (linkedElement == null ? "(null)" : linkedElement.Name));
                text.AppendLine("Linked CurveElement: " + (linkedElement is CurveElement));
                text.AppendLine("Linked LocationCurve: " + (linkedElement != null && linkedElement.Location is LocationCurve));
            }

            Curve curve = GetCurveFromReference(document, reference);
            text.AppendLine("Curve Found: " + (curve != null));
            if (curve != null)
            {
                text.AppendLine("Curve Start: " + FormatPoint(curve.GetEndPoint(0)));
                text.AppendLine("Curve End: " + FormatPoint(curve.GetEndPoint(1)));
            }

            return text.ToString();
        }

        private static string GetCategoryName(Element element)
        {
            Category category = element == null ? null : element.Category;
            return category == null ? "(null)" : category.Name;
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
                return "(null)";

            return point.X.ToString("0.###", CultureInfo.InvariantCulture)
                + ", "
                + point.Y.ToString("0.###", CultureInfo.InvariantCulture)
                + ", "
                + point.Z.ToString("0.###", CultureInfo.InvariantCulture);
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
            if (linkedElement == null)
                return null;

            Transform linkTransform = GetBestLinkTransform(linkInstance, reference.GlobalPoint, GetCurveFromElement(linkedElement));
            XYZ linkedPickedPoint = GetInversePoint(linkTransform, reference.GlobalPoint);
            Curve curve = GetCurveFromElement(linkedElement);
            if (curve == null)
                curve = GetCurveFromLinkedGeometry(linkedElement, reference, linkedPickedPoint);
            if (curve == null)
                return null;

            curve = TransformCurveIfCloser(linkedElement, curve, linkedPickedPoint);
            linkTransform = GetBestLinkTransform(linkInstance, reference.GlobalPoint, curve);
            return curve.CreateTransformed(linkTransform);
        }

        private static Curve GetCurveFromLinkedGeometry(Element linkedElement, Reference reference, XYZ linkedPickedPoint)
        {
            GeometryObject geometryObject = null;
            try
            {
                Reference linkedReference = reference.CreateReferenceInLink();
                geometryObject = linkedElement.GetGeometryObjectFromReference(linkedReference);
            }
            catch
            {
                geometryObject = null;
            }

            return GetCurveFromGeometryObject(geometryObject, linkedPickedPoint);
        }

        private static Transform GetBestLinkTransform(RevitLinkInstance linkInstance, XYZ pickedPoint, Curve linkedCurve)
        {
            Transform instanceTransform = linkInstance.GetTransform();
            Transform totalTransform = linkInstance.GetTotalTransform();
            if (linkedCurve == null || pickedPoint == null)
                return totalTransform ?? instanceTransform ?? Transform.Identity;

            Transform bestTransform = totalTransform ?? instanceTransform ?? Transform.Identity;
            double bestDistance = DistanceToTransformedCurve(linkedCurve, bestTransform, pickedPoint);

            double instanceDistance = DistanceToTransformedCurve(linkedCurve, instanceTransform, pickedPoint);
            if (instanceDistance < bestDistance)
            {
                bestTransform = instanceTransform;
            }

            return bestTransform;
        }

        private static double DistanceToTransformedCurve(Curve curve, Transform transform, XYZ point)
        {
            if (curve == null || transform == null)
                return double.MaxValue;

            try
            {
                return DistanceToCurve(curve.CreateTransformed(transform), point);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        private static XYZ GetInversePoint(Transform transform, XYZ point)
        {
            if (transform == null || point == null)
                return point;

            try
            {
                return transform.Inverse.OfPoint(point);
            }
            catch
            {
                return point;
            }
        }

        public static int PlaceFixtures(Document document, Curve centerline, PlacementSettings settings)
        {
            FamilySymbol symbol = document.GetElement(settings.FamilySymbolId) as FamilySymbol;
            if (symbol == null)
                throw new InvalidOperationException("선택한 등기구 패밀리 타입을 찾을 수 없습니다.");

            List<PathSegment> path = BuildPath(centerline);
            if (path.Count == 0)
                throw new InvalidOperationException("중심선 길이를 계산할 수 없습니다.");

            double totalLength = path[path.Count - 1].EndDistance;
            double start = Math.Max(0.0, settings.StartDistanceMm * FeetPerMm);
            double end = settings.EndDistanceMm <= 0
                ? totalLength
                : Math.Min(settings.EndDistanceMm * FeetPerMm, totalLength);
            double spacing = settings.SpacingMm * FeetPerMm;

            if (spacing <= 0)
                throw new InvalidOperationException("설치 간격은 0보다 커야 합니다.");

            if (end < start)
                throw new InvalidOperationException("종료 거리는 시작 거리보다 커야 합니다.");

            int placed = 0;
            using (var transaction = new Transaction(document, "터널 전등 자동배치"))
            {
                transaction.Start();

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                for (double distance = start; distance <= end + 1e-8; distance += spacing)
                {
                    PathPoint pathPoint = EvaluatePath(path, distance);
                    XYZ tangent = FlattenAndNormalize(pathPoint.Tangent);
                    if (tangent == null)
                        continue;

                    XYZ placementPoint = settings.PlaceOnSelectedLine
                        ? pathPoint.Point
                        : GetOffsetPoint(pathPoint.Point, tangent, settings);

                    FamilyInstance instance = document.Create.NewFamilyInstance(
                        placementPoint,
                        symbol,
                        StructuralType.NonStructural);

                    document.Regenerate();
                    MoveInstanceToPoint(document, instance, placementPoint);
                    document.Regenerate();
                    RotateAroundZ(document, instance, tangent);
                    document.Regenerate();
                    if (settings.PlaceOnSelectedLine)
                        MoveInstanceVisualCenterToPoint(document, instance, placementPoint);
                    WriteParameters(instance, settings, distance / FeetPerMm);
                    placed++;
                }

                transaction.Commit();
            }

            return placed;
        }

        private static Curve GetCurveFromGeometryObject(GeometryObject geometryObject, XYZ pickedPoint)
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
            if (polyLine != null)
                return GetNearestPolyLineSegment(polyLine, pickedPoint);

            return null;
        }

        private static Curve TransformCurveIfCloser(Element element, Curve curve, XYZ pickedPoint)
        {
            if (element == null || curve == null || pickedPoint == null)
                return curve;

            Transform transform = GetElementTransform(element);
            if (transform == null || transform.IsIdentity)
                return curve;

            Curve transformedCurve = null;
            try
            {
                transformedCurve = curve.CreateTransformed(transform);
            }
            catch
            {
                return curve;
            }

            double originalDistance = DistanceToCurve(curve, pickedPoint);
            double transformedDistance = DistanceToCurve(transformedCurve, pickedPoint);
            return transformedDistance < originalDistance ? transformedCurve : curve;
        }

        private static Transform GetElementTransform(Element element)
        {
            ImportInstance importInstance = element as ImportInstance;
            if (importInstance != null)
                return importInstance.GetTotalTransform();

            Instance instance = element as Instance;
            if (instance != null)
                return instance.GetTransform();

            return Transform.Identity;
        }

        private static double DistanceToCurve(Curve curve, XYZ point)
        {
            if (curve == null || point == null)
                return double.MaxValue;

            IntersectionResult projection = curve.Project(point);
            return projection == null ? double.MaxValue : projection.XYZPoint.DistanceTo(point);
        }

        private static Curve AlignCurveNearPickedPoint(Curve curve, XYZ pickedPoint)
        {
            if (curve == null || pickedPoint == null)
                return curve;

            IntersectionResult projection = curve.Project(pickedPoint);
            if (projection == null)
                return curve;

            XYZ moveVector = pickedPoint - projection.XYZPoint;
            if (moveVector.GetLength() < 1e-6)
                return curve;

            return curve.CreateTransformed(Transform.CreateTranslation(moveVector));
        }

        private static Curve GetNearestPolyLineSegment(PolyLine polyLine, XYZ pickedPoint)
        {
            IList<XYZ> points = polyLine.GetCoordinates();
            if (points == null || points.Count < 2)
                return null;

            Curve bestCurve = null;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) < 1e-9)
                    continue;

                Line line = Line.CreateBound(points[i], points[i + 1]);
                IntersectionResult projection = pickedPoint == null ? null : line.Project(pickedPoint);
                double distance = projection == null ? 0.0 : projection.XYZPoint.DistanceTo(pickedPoint);

                if (bestCurve == null || distance < bestDistance)
                {
                    bestCurve = line;
                    bestDistance = distance;
                }
            }

            return bestCurve;
        }

        private static XYZ GetOffsetPoint(XYZ basePoint, XYZ tangent, PlacementSettings settings)
        {
            XYZ lateral = GetLateralDirection(tangent, settings.Side);
            double offset = settings.OffsetMm * FeetPerMm;
            double height = settings.HeightMm * FeetPerMm;
            return basePoint + lateral.Multiply(offset) + XYZ.BasisZ.Multiply(height);
        }

        private static void MoveInstanceToPoint(Document document, FamilyInstance instance, XYZ targetPoint)
        {
            LocationPoint locationPoint = instance == null ? null : instance.Location as LocationPoint;
            if (locationPoint == null)
                return;

            XYZ moveVector = targetPoint - locationPoint.Point;
            if (moveVector.GetLength() < 1e-8)
                return;

            ElementTransformUtils.MoveElement(document, instance.Id, moveVector);
        }

        private static void MoveInstanceVisualCenterToPoint(Document document, FamilyInstance instance, XYZ targetPoint)
        {
            if (instance == null || targetPoint == null)
                return;

            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            LocationPoint locationPoint = instance.Location as LocationPoint;
            if (box == null || locationPoint == null)
                return;

            XYZ visualCenter = new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);

            XYZ moveVector = new XYZ(
                targetPoint.X - visualCenter.X,
                targetPoint.Y - visualCenter.Y,
                targetPoint.Z - locationPoint.Point.Z);

            if (moveVector.GetLength() < 1e-8)
                return;

            ElementTransformUtils.MoveElement(document, instance.Id, moveVector);
        }

        private static List<PathSegment> BuildPath(Curve curve)
        {
            IList<XYZ> points = curve.Tessellate();
            var result = new List<PathSegment>();
            double cumulative = 0.0;

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

            return result;
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

        private static XYZ GetLateralDirection(XYZ tangent, PlacementSide side)
        {
            if (side == PlacementSide.Left)
                return XYZ.BasisZ.CrossProduct(tangent).Normalize();

            if (side == PlacementSide.Right)
                return tangent.CrossProduct(XYZ.BasisZ).Normalize();

            return XYZ.Zero;
        }

        private static void RotateAroundZ(Document document, FamilyInstance instance, XYZ targetX)
        {
            XYZ currentX = FlattenAndNormalize(instance.HandOrientation);
            if (currentX == null)
                currentX = FlattenAndNormalize(instance.FacingOrientation);
            if (currentX == null)
                currentX = XYZ.BasisX;

            double angle = SignedAngle(currentX, targetX);
            if (Math.Abs(angle) < 1e-8)
                return;

            XYZ pivot = ((LocationPoint)instance.Location).Point;
            Line axis = Line.CreateBound(pivot, pivot + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(document, instance.Id, axis, angle);
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
                return null;

            XYZ flat = new XYZ(vector.X, vector.Y, 0.0);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        private static double SignedAngle(XYZ from, XYZ to)
        {
            double dot = Math.Max(-1.0, Math.Min(1.0, from.DotProduct(to)));
            double angle = Math.Acos(dot);
            double crossZ = from.X * to.Y - from.Y * to.X;
            return crossZ < 0 ? -angle : angle;
        }

        private static void WriteParameters(FamilyInstance instance, PlacementSettings settings, double stationMm)
        {
            string sideText = GetSideText(settings.Side);
            SetParameter(instance, settings.StationParameterName, FormatStation(stationMm), stationMm);
            SetParameter(instance, settings.SegmentParameterName, settings.SegmentName ?? string.Empty, 0.0);
            SetParameter(instance, settings.DirectionParameterName, sideText, 0.0);
            SetParameter(instance, settings.OffsetParameterName, settings.OffsetMm.ToString("0.###", CultureInfo.InvariantCulture), settings.OffsetMm);
            SetParameter(instance, settings.HeightParameterName, settings.HeightMm.ToString("0.###", CultureInfo.InvariantCulture), settings.HeightMm);
        }

        private static void SetParameter(Element element, string parameterName, string textValue, double numericMmValue)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                return;

            Parameter parameter = element.LookupParameter(parameterName.Trim());
            if (parameter == null || parameter.IsReadOnly)
                return;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(textValue ?? string.Empty);
                    break;
                case StorageType.Double:
                    parameter.Set(numericMmValue * FeetPerMm);
                    break;
                case StorageType.Integer:
                    parameter.Set((int)Math.Round(numericMmValue));
                    break;
            }
        }

        private static string FormatStation(double stationMm)
        {
            double stationM = stationMm / 1000.0;
            int km = (int)Math.Floor(stationM / 1000.0);
            double meter = stationM - km * 1000.0;
            return km.ToString(CultureInfo.InvariantCulture) + "+" + meter.ToString("000.000", CultureInfo.InvariantCulture);
        }

        private static string GetSideText(PlacementSide side)
        {
            if (side == PlacementSide.Left)
                return "Left";
            if (side == PlacementSide.Right)
                return "Right";
            return "Center";
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
