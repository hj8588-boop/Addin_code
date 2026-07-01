using System.Collections.Generic;
using Autodesk.Revit.DB;
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
                    "Select cable trays and cable tray fittings to generate cables in.");

                var cablePathElements = new List<Element>();
                foreach (Reference picked in pickedReferences)
                {
                    Element element = document.GetElement(picked.ElementId);
                    if (CableGeneratorService.IsSupportedCablePathElement(element))
                        cablePathElements.Add(element);
                }

                if (cablePathElements.Count == 0)
                {
                    TaskDialog.Show("Cable Generator", "Select at least one cable tray or cable tray fitting.");
                    return Result.Cancelled;
                }

                using (var form = new CableGeneratorForm(document))
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    int createdCount = 0;
                    foreach (Element element in cablePathElements)
                        createdCount += CableGeneratorService.CreateCables(document, element, form.Settings);

                    TaskDialog.Show(
                        "Cable Generator",
                        createdCount + " cables were created in " + cablePathElements.Count + " selected elements.");
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
