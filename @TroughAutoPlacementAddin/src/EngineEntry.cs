using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;

namespace TroughAutoPlacementAddin
{
    public static class EngineEntry
    {
        public static Result Run(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;
            if (document == null)
            {
                TaskDialog.Show("\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58", "Open a Revit document first.");
                return Result.Cancelled;
            }

            try
            {
                IList<Reference> mainReferences = uiDocument.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.PointOnElement,
                    new EdgeSelectionFilter(document),
                    "1) Select Main DWG/Revit edges. Use TAB if needed.");

                if (mainReferences == null || mainReferences.Count == 0)
                    return Result.Cancelled;

                IList<Reference> sideReferences = uiDocument.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.PointOnElement,
                    new EdgeSelectionFilter(document),
                    "2) Select Side DWG/Revit edges. Use TAB if needed.");

                if (sideReferences == null || sideReferences.Count == 0)
                    return Result.Cancelled;

                IList<Curve> mainCurves = GetCurvesFromReferences(document, mainReferences);
                IList<Curve> sideCurves = GetCurvesFromReferences(document, sideReferences);
                if (mainCurves.Count == 0 || sideCurves.Count == 0)
                {
                    TaskDialog.Show("\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58", "Could not read usable curves from the selected edges.");
                    return Result.Cancelled;
                }

                FamilyPlacementSettings settings;
                using (var form = new TroughFamilyPlacementForm(document))
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    settings = form.Settings;
                }

                IList<ElementId> placedIds = TroughFamilyPlacementService.PlaceFamilies(
                    document,
                    mainCurves,
                    sideCurves,
                    settings);

                TaskDialog.Show("\uD2B8\uB85C\uD504 \uC790\uB3D9 \uBC30\uCE58", "Placed " + placedIds.Count + " trough family instances.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        private static IList<Curve> GetCurvesFromReferences(Document document, IList<Reference> references)
        {
            var curves = new List<Curve>();
            foreach (Reference reference in references)
            {
                foreach (Curve curve in TroughFamilyPlacementService.GetCurvesFromReference(document, reference))
                {
                    if (curve != null)
                        curves.Add(curve);
                }
            }

            return curves;
        }
    }
}
