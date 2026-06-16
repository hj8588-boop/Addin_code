using System.Collections.Generic;
using System.Linq;
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

            try
            {
                IList<Reference> pickedReferences = uiDocument.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.PointOnElement,
                    new TunnelCenterlineSelectionFilterV2(document),
                    "터널 중심선으로 사용할 Model Line, 그룹 내부 선, Generic Model edge를 여러 개 선택하세요.");

                if (pickedReferences == null || pickedReferences.Count == 0)
                {
                    TaskDialog.Show("터널 전등 자동배치", "선택된 선이 없습니다.");
                    return Result.Cancelled;
                }

                var centerlines = new List<Curve>();
                XYZ preferredDirection = null;
                foreach (Reference picked in pickedReferences)
                {
                    Curve centerline = TunnelLightingPlacementServiceV2.GetCurveFromReference(document, picked);
                    if (centerline == null)
                        continue;

                    centerlines.Add(centerline);
                    if (preferredDirection == null)
                        preferredDirection = TunnelLightingPlacementServiceV2.GetCurveDirection(centerline);
                }

                if (centerlines.Count == 0)
                {
                    TaskDialog.Show("터널 전등 자동배치", "선택한 객체에서 배치 기준 Curve를 읽을 수 없습니다.");
                    return Result.Cancelled;
                }

                var previewIds = new List<ElementId>();
                PlacementSettings settings;
                bool keepPreview = false;

                using (var form = new TunnelLightingPlacementForm(document))
                {
                    form.PreviewPlacement = previewSettings =>
                    {
                        TunnelLightingPlacementServiceV2.DeletePreviewFixtures(document, previewIds);
                        previewIds = TunnelLightingPlacementServiceV2
                            .PreviewFixtures(document, centerlines, previewSettings, preferredDirection)
                            .ToList();
                        return previewIds.Count;
                    };

                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        TunnelLightingPlacementServiceV2.DeletePreviewFixtures(document, previewIds);
                        return Result.Cancelled;
                    }

                    settings = form.Settings;
                    keepPreview = form.PreviewIsCurrent && previewIds.Count > 0;
                }

                int placedCount;
                if (keepPreview)
                {
                    placedCount = previewIds.Count;
                }
                else
                {
                    TunnelLightingPlacementServiceV2.DeletePreviewFixtures(document, previewIds);
                    placedCount = TunnelLightingPlacementServiceV2.PlaceFixtures(document, centerlines, settings, preferredDirection);
                }

                TaskDialog.Show("터널 전등 자동배치", centerlines.Count + "개의 선택 선에 " + placedCount + "개의 조명기구를 배치했습니다.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }
    }
}
