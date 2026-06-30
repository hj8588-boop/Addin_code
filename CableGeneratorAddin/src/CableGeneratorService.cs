using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace CableGeneratorAddin
{
    public static class CableGeneratorService
    {
        private const double FeetPerMm = 1.0 / 304.8;

        public static int CreateCables(Document document, CableTray tray, CableGeneratorSettings settings)
        {
            if (document == null)
                throw new InvalidOperationException("Open a Revit document first.");
            if (tray == null)
                throw new InvalidOperationException("Select a cable tray first.");
            if (settings == null || settings.ConduitTypeId == null || settings.ConduitTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Select a conduit type first.");

            LocationCurve locationCurve = tray.Location as LocationCurve;
            if (locationCurve == null || locationCurve.Curve == null)
                throw new InvalidOperationException("The selected cable tray does not have a usable path.");

            Level level = GetLevel(document, tray);
            if (level == null)
                throw new InvalidOperationException("Cannot find a level for the new cables.");

            double diameter = settings.CableDiameterMm * FeetPerMm;
            double gap = settings.GapMm * FeetPerMm;
            double trayOffset = settings.TrayOffsetMm * FeetPerMm;
            double trayWidth = GetLengthParameter(tray, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
            double trayHeight = GetLengthParameter(tray, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            if (trayWidth <= 0.0)
                throw new InvalidOperationException("The selected cable tray does not have a valid width.");
            if (trayHeight <= 0.0)
                trayHeight = diameter + (trayOffset * 2.0);

            double requiredWidth = (settings.CableCount * diameter) + ((settings.CableCount - 1) * gap) + (trayOffset * 2.0);
            if (requiredWidth > trayWidth + 1e-9)
                throw new InvalidOperationException("The cable layout is wider than the selected cable tray. Reduce count, diameter, gap, or offset.");

            Curve trayCurve = locationCurve.Curve;
            XYZ start = trayCurve.GetEndPoint(0);
            XYZ end = trayCurve.GetEndPoint(1);
            XYZ tangent = FlattenAndNormalize(end - start);
            if (tangent == null)
                throw new InvalidOperationException("The selected cable tray path is too short.");

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
    }
}
