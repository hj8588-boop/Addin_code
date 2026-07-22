using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace CableGeneratorAddin
{
    public static class CableGeneratorService
    {
        private const double FeetPerMm = 1.0 / 304.8;

        public static bool IsSupportedCablePathElement(Element element)
        {
            if (element is CableTray)
                return true;

            Category category = element == null ? null : element.Category;
            return category != null
                && category.Id.Value == (long)BuiltInCategory.OST_CableTrayFitting;
        }

        public static int CreateCables(Document document, Element cablePathElement, CableGeneratorSettings settings)
        {
            if (document == null)
                throw new InvalidOperationException("Open a Revit document first.");
            if (!IsSupportedCablePathElement(cablePathElement))
                throw new InvalidOperationException("Select a cable tray or cable tray fitting first.");
            if (settings == null || settings.ConduitTypeId == null || settings.ConduitTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Select a conduit type first.");

            if (IsCableTrayFitting(cablePathElement))
                return CreateCableFittings(document, cablePathElement, settings);

            CablePath path = GetCablePath(cablePathElement);
            if (path == null)
                throw new InvalidOperationException("The selected cable tray or fitting does not have a usable path.");

            Level level = GetLevel(document, cablePathElement);
            if (level == null)
                throw new InvalidOperationException("Cannot find a level for the new cables.");

            double diameter = settings.CableDiameterMm * FeetPerMm;
            double gap = settings.GapMm * FeetPerMm;
            double trayOffset = settings.TrayOffsetMm * FeetPerMm;
            double trayWidth = path.Width;
            double trayHeight = path.Height;

            if (trayWidth <= 0.0)
                throw new InvalidOperationException("The selected cable tray or fitting does not have a valid width.");
            if (trayHeight <= 0.0)
                trayHeight = diameter + (trayOffset * 2.0);

            double requiredWidth = (settings.CableCount * diameter) + ((settings.CableCount - 1) * gap) + (trayOffset * 2.0);
            if (requiredWidth > trayWidth + 1e-9)
                throw new InvalidOperationException("The cable layout is wider than the selected cable tray. Reduce count, diameter, gap, or offset.");

            XYZ start = path.Start;
            XYZ end = path.End;
            XYZ tangent = FlattenAndNormalize(end - start);
            if (tangent == null)
                throw new InvalidOperationException("The selected cable tray or fitting path is too short.");

            XYZ side = new XYZ(-tangent.Y, tangent.X, 0.0);
            double firstSideOffset = (-trayWidth / 2.0) + trayOffset + (diameter / 2.0);
            double verticalOffset = (-trayHeight / 2.0) + trayOffset + (diameter / 2.0);

            var created = new List<ElementId>();
            using (var transaction = new Transaction(document, "Create Cables"))
            {
                transaction.Start();

                for (int i = 0; i < settings.CableCount; i++)
                {
                    double sideOffset = firstSideOffset + (i * (diameter + gap));
                    XYZ cableStart = start + side.Multiply(sideOffset) + XYZ.BasisZ.Multiply(verticalOffset);
                    XYZ cableEnd = end + side.Multiply(sideOffset) + XYZ.BasisZ.Multiply(verticalOffset);

                    Conduit conduit = Conduit.Create(document, settings.ConduitTypeId, cableStart, cableEnd, level.Id);
                    SetLengthParameter(conduit, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, diameter);
                    created.Add(conduit.Id);
                }

                transaction.Commit();
            }

            return created.Count;
        }

        private static int CreateCableFittings(Document document, Element fittingElement, CableGeneratorSettings settings)
        {
            FittingPath path = GetFittingPathFromConnectors(fittingElement);
            if (path == null)
                throw new InvalidOperationException("The selected cable tray fitting does not have two usable connectors.");

            Level level = GetLevel(document, fittingElement);
            if (level == null)
                throw new InvalidOperationException("Cannot find a level for the new cable fittings.");

            double diameter = settings.CableDiameterMm * FeetPerMm;
            double gap = settings.GapMm * FeetPerMm;
            double trayOffset = settings.TrayOffsetMm * FeetPerMm;
            double trayWidth = path.Width;
            double trayHeight = path.Height;

            if (trayWidth <= 0.0)
                throw new InvalidOperationException("The selected cable tray fitting does not have a valid width.");
            if (trayHeight <= 0.0)
                trayHeight = diameter + (trayOffset * 2.0);

            double requiredWidth = (settings.CableCount * diameter) + ((settings.CableCount - 1) * gap) + (trayOffset * 2.0);
            if (requiredWidth > trayWidth + 1e-9)
                throw new InvalidOperationException("The cable layout is wider than the selected cable tray fitting. Reduce count, diameter, gap, or offset.");

            XYZ pathDirection = FlattenAndNormalize(path.Second.Origin - path.First.Origin);
            if (pathDirection == null)
                throw new InvalidOperationException("The selected cable tray fitting path is too short.");

            XYZ side = new XYZ(-pathDirection.Y, pathDirection.X, 0.0);
            double firstSideOffset = (-trayWidth / 2.0) + trayOffset + (diameter / 2.0);
            double verticalOffset = (-trayHeight / 2.0) + trayOffset + (diameter / 2.0);
            double stubLength = Math.Min(path.First.Origin.DistanceTo(path.Second.Origin) * 0.35, Math.Max(200.0 * FeetPerMm, diameter * 6.0));

            int createdCableCount = 0;
            using (var transaction = new Transaction(document, "Create Cable Fittings"))
            {
                transaction.Start();

                for (int i = 0; i < settings.CableCount; i++)
                {
                    double sideOffset = firstSideOffset + (i * (diameter + gap));
                    XYZ offset = side.Multiply(sideOffset) + XYZ.BasisZ.Multiply(verticalOffset);

                    XYZ firstOuter = path.First.Origin + offset;
                    XYZ secondOuter = path.Second.Origin + offset;
                    XYZ firstDirection = GetConnectorDirectionToward(path.First, path.Second.Origin);
                    XYZ secondDirection = GetConnectorDirectionToward(path.Second, path.First.Origin);

                    XYZ firstInner = firstOuter + firstDirection.Multiply(stubLength);
                    XYZ secondInner = secondOuter + secondDirection.Multiply(stubLength);

                    Conduit firstConduit = Conduit.Create(document, settings.ConduitTypeId, firstOuter, firstInner, level.Id);
                    Conduit secondConduit = Conduit.Create(document, settings.ConduitTypeId, secondOuter, secondInner, level.Id);
                    SetLengthParameter(firstConduit, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, diameter);
                    SetLengthParameter(secondConduit, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, diameter);

                    Connector firstInnerConnector = GetClosestConnector(firstConduit, firstInner);
                    Connector secondInnerConnector = GetClosestConnector(secondConduit, secondInner);
                    CreateConduitFitting(document, firstInnerConnector, secondInnerConnector);
                    createdCableCount++;
                }

                transaction.Commit();
            }

            return createdCableCount;
        }

        private static CablePath GetCablePath(Element element)
        {
            LocationCurve locationCurve = element.Location as LocationCurve;
            if (locationCurve != null && locationCurve.Curve != null)
            {
                Curve curve = locationCurve.Curve;
                return new CablePath(
                    curve.GetEndPoint(0),
                    curve.GetEndPoint(1),
                    GetLengthParameter(element, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM),
                    GetLengthParameter(element, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM));
            }

            return GetCablePathFromConnectors(element);
        }

        private static CablePath GetCablePathFromConnectors(Element element)
        {
            FittingPath fittingPath = GetFittingPathFromConnectors(element);
            if (fittingPath == null)
                return null;

            return new CablePath(fittingPath.First.Origin, fittingPath.Second.Origin, fittingPath.Width, fittingPath.Height);
        }

        private static FittingPath GetFittingPathFromConnectors(Element element)
        {
            IList<Connector> connectors = GetConnectors(element)
                .Where(connector => connector != null && connector.Origin != null)
                .ToList();

            if (connectors.Count < 2)
                return null;

            Connector first = null;
            Connector second = null;
            double longestDistance = 0.0;
            for (int i = 0; i < connectors.Count; i++)
            {
                for (int j = i + 1; j < connectors.Count; j++)
                {
                    double distance = connectors[i].Origin.DistanceTo(connectors[j].Origin);
                    if (distance > longestDistance)
                    {
                        longestDistance = distance;
                        first = connectors[i];
                        second = connectors[j];
                    }
                }
            }

            if (first == null || second == null)
                return null;

            double width = GetLengthParameter(element, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
            double height = GetLengthParameter(element, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
            if (width <= 0.0)
                width = Math.Max(GetConnectorWidth(first), GetConnectorWidth(second));
            if (height <= 0.0)
                height = Math.Max(GetConnectorHeight(first), GetConnectorHeight(second));

            return new FittingPath(first, second, width, height);
        }

        private static IList<Connector> GetConnectors(Element element)
        {
            var result = new List<Connector>();

            MEPCurve mepCurve = element as MEPCurve;
            if (mepCurve != null && mepCurve.ConnectorManager != null)
            {
                foreach (Connector connector in mepCurve.ConnectorManager.Connectors)
                    result.Add(connector);
                return result;
            }

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null && familyInstance.MEPModel != null && familyInstance.MEPModel.ConnectorManager != null)
            {
                foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
                    result.Add(connector);
            }

            return result;
        }

        private static double GetConnectorWidth(Connector connector)
        {
            try
            {
                return connector.Width;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetConnectorHeight(Connector connector)
        {
            try
            {
                return connector.Height;
            }
            catch
            {
                return 0.0;
            }
        }

        private static XYZ GetConnectorDirectionToward(Connector connector, XYZ target)
        {
            XYZ direction = GetConnectorDirection(connector);
            XYZ fallback = FlattenAndNormalize(target - connector.Origin) ?? XYZ.BasisX;
            if (direction == null)
                return fallback;

            return direction.DotProduct(fallback) >= 0.0 ? direction : direction.Negate();
        }

        private static XYZ GetConnectorDirection(Connector connector)
        {
            try
            {
                Transform coordinateSystem = connector.CoordinateSystem;
                if (coordinateSystem == null)
                    return null;

                XYZ direction = coordinateSystem.BasisZ;
                return direction == null || direction.GetLength() < 1e-9 ? null : direction.Normalize();
            }
            catch
            {
                return null;
            }
        }

        private static Connector GetClosestConnector(MEPCurve curve, XYZ point)
        {
            Connector closest = null;
            double closestDistance = double.MaxValue;

            if (curve == null || curve.ConnectorManager == null)
                return null;

            foreach (Connector connector in curve.ConnectorManager.Connectors)
            {
                double distance = connector.Origin.DistanceTo(point);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = connector;
                }
            }

            return closest;
        }

        private static void CreateConduitFitting(Document document, Connector first, Connector second)
        {
            if (first == null || second == null)
                throw new InvalidOperationException("Cannot find conduit connectors for the fitting.");

            if (ShouldUseUnionFitting(first, second))
            {
                document.Create.NewUnionFitting(first, second);
                return;
            }

            try
            {
                document.Create.NewElbowFitting(first, second);
            }
            catch
            {
                document.Create.NewUnionFitting(first, second);
            }
        }

        private static bool ShouldUseUnionFitting(Connector first, Connector second)
        {
            XYZ firstDirection = GetConnectorDirection(first);
            XYZ secondDirection = GetConnectorDirection(second);
            if (firstDirection == null || secondDirection == null)
                return false;

            return Math.Abs(Math.Abs(firstDirection.DotProduct(secondDirection)) - 1.0) < 0.01;
        }

        private static bool IsCableTrayFitting(Element element)
        {
            Category category = element == null ? null : element.Category;
            return category != null
                && category.Id.Value == (long)BuiltInCategory.OST_CableTrayFitting;
        }

        private static Level GetLevel(Document document, Element source)
        {
            if (source.LevelId != null && source.LevelId != ElementId.InvalidElementId)
            {
                Level sourceLevel = document.GetElement(source.LevelId) as Level;
                if (sourceLevel != null)
                    return sourceLevel;
            }

            ViewPlan viewPlan = document.ActiveView as ViewPlan;
            if (viewPlan != null && viewPlan.GenLevel != null)
                return viewPlan.GenLevel;

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .FirstElement() as Level;
        }

        private static double GetLengthParameter(Element element, BuiltInParameter parameterId)
        {
            Parameter parameter = element == null ? null : element.get_Parameter(parameterId);
            return parameter == null ? 0.0 : parameter.AsDouble();
        }

        private static void SetLengthParameter(Element element, BuiltInParameter parameterId, double value)
        {
            Parameter parameter = element == null ? null : element.get_Parameter(parameterId);
            if (parameter != null && !parameter.IsReadOnly)
                parameter.Set(value);
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
                return null;

            XYZ flat = new XYZ(vector.X, vector.Y, 0.0);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        private class CablePath
        {
            public CablePath(XYZ start, XYZ end, double width, double height)
            {
                Start = start;
                End = end;
                Width = width;
                Height = height;
            }

            public XYZ Start { get; private set; }
            public XYZ End { get; private set; }
            public double Width { get; private set; }
            public double Height { get; private set; }
        }

        private class FittingPath
        {
            public FittingPath(Connector first, Connector second, double width, double height)
            {
                First = first;
                Second = second;
                Width = width;
                Height = height;
            }

            public Connector First { get; private set; }
            public Connector Second { get; private set; }
            public double Width { get; private set; }
            public double Height { get; private set; }
        }
    }
}
