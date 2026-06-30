using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CableGeneratorAddin
{
    public static class EngineEntry
    {
        public static Result Run(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;
            if (document == null)
            {
                TaskDialog.Show("Cable Generator", "Open a Revit document first.");
                return Result.Cancelled;
            }

            try
            {
                IList<Reference> pickedReferences = uiDocument.Selection.PickObjects(
                    ObjectType.Element,
                    new CableTraySelectionFilter(),
                    "Select cable trays to generate cables in.");

                var trays = new List<CableTray>();
                foreach (Reference picked in pickedReferences)
                {
                    CableTray tray = document.GetElement(picked.ElementId) as CableTray;
                    if (tray != null)
                        trays.Add(tray);
                }

                if (trays.Count == 0)
                {
                    TaskDialog.Show("Cable Generator", "Select at least one cable tray.");
                    return Result.Cancelled;
                }

                using (var form = new CableGeneratorForm(document))
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    int createdCount = 0;
                    foreach (CableTray tray in trays)
                        createdCount += CableGeneratorService.CreateCables(document, tray, form.Settings);

                    TaskDialog.Show(
                        "Cable Generator",
                        createdCount + " cables were created in " + trays.Count + " cable trays.");
                }

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }
    }
}
