using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TunnelLightingPlacementAddin
{
    public static class TunnelLightingPlacementService
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
            double offset = settings.OffsetMm * FeetPerMm;
            double height = settings.HeightMm * FeetPerMm;

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

                    XYZ lateral = GetLateralDirection(tangent, settings.Side);
                    XYZ placementPoint = pathPoint.Point + lateral.Multiply(offset) + XYZ.BasisZ.Multiply(height);

                    FamilyInstance instance = document.Create.NewFamilyInstance(
                        placementPoint,
                        symbol,
                        StructuralType.NonStructural);

                    document.Regenerate();
                    RotateAroundZ(document, instance, tangent);
                    WriteParameters(instance, settings, distance / FeetPerMm);
                    placed++;
                }

                transaction.Commit();
            }

            return placed;
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
