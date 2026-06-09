using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;

namespace TunnelLightingPlacementAddin
{
    public static class EngineEntry
    {
        public static Result Run(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;
            if (document == null)
            {
                TaskDialog.Show("터널 전등 자동배치", "Revit 문서를 먼저 열어주세요.");
                return Result.Cancelled;
            }

            PlacementSettings settings;
            using (var form = new TunnelLightingPlacementForm(document))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                settings = form.Settings;
            }

            try
            {
                Reference picked = uiDocument.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new TunnelCenterlineSelectionFilter(),
                    "터널 중심선 Model Line 또는 Curve 요소를 선택하세요.");

                Curve centerline = TunnelLightingPlacementService.GetCurveFromElement(document.GetElement(picked));
                if (centerline == null)
                {
                    TaskDialog.Show("터널 전등 자동배치", "선택한 요소에서 Curve를 읽을 수 없습니다.");
                    return Result.Cancelled;
                }

                int placedCount = TunnelLightingPlacementService.PlaceFixtures(document, centerline, settings);
                TaskDialog.Show("터널 전등 자동배치", placedCount + "개의 등기구를 배치했습니다.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }
    }
}
