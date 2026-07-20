using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;

namespace TunnelCableTrayPlacementAddin
{
    public static class EngineEntry
    {
        public static Result Run(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;
            if (document == null)
            {
                TaskDialog.Show("Tunnel Cable Tray Placement", "Revit 문서를 먼저 열어주세요.");
                return Result.Cancelled;
            }

            try
            {
                Reference pickedReference = uiDocument.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.PointOnElement,
                    new TunnelCenterlineSelectionFilter(document),
                    "케이블 트레이 기준선 하나를 선택하세요. 연결된 선은 자동으로 추적합니다.");

                if (pickedReference == null)
                    return Result.Cancelled;

                IList<Curve> tracedCurves = ConnectedCurveTracer.Trace(document, pickedReference, 50.0);
                IList<Curve> centerlineCurves = tracedCurves;

                var centerlines = centerlineCurves
                    .Where(curve => curve != null)
                    .ToList();

                if (centerlines.Count == 0)
                {
                    TaskDialog.Show("Tunnel Cable Tray Placement", "선택한 선에서 연결 경로를 찾을 수 없습니다.");
                    return Result.Cancelled;
                }

                XYZ preferredStartPoint = pickedReference.GlobalPoint;
                XYZ preferredDirection = TunnelCableTrayPlacementService.GetCurveDirection(centerlines[0]);
                var previewIds = new List<ElementId>();
                PlacementSettings settings;
                bool keepPreview;

                using (var form = new TunnelCableTrayPlacementForm(document))
                {
                    form.Text = "Tunnel Cable Tray Placement - "
                        + ConnectedCurveTracer.LastConnectedCount
                        + "\uAC1C \uC5F0\uACB0\uC120 / "
                        + System.Math.Round(ConnectedCurveTracer.LastPathLengthMm).ToString("N0")
                        + " mm";
                    form.PreviewPlacement = previewSettings =>
                    {
                        TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
                        previewIds = TunnelCableTrayPlacementService
                            .PreviewTrays(document, centerlines, previewSettings, preferredDirection, preferredStartPoint)
                            .ToList();
                        return previewIds.Count;
                    };

                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
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
                    TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
                    placedCount = TunnelCableTrayPlacementService.PlaceTrays(
                        document,
                        centerlines,
                        settings,
                        preferredDirection,
                        preferredStartPoint);
                }

                TaskDialog.Show(
                    "Tunnel Cable Tray Placement",
                    centerlines.Count + "개의 연결 선을 추적하여 " + placedCount + "개의 케이블 트레이를 배치했습니다.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }

        private static Result RunLegacy(UIApplication application)
        {
            UIDocument uiDocument = application.ActiveUIDocument;
            Document document = uiDocument == null ? null : uiDocument.Document;
            if (document == null)
            {
                TaskDialog.Show("Tunnel Cable Tray Placement", "Revit 문서를 먼저 열어주세요.");
                return Result.Cancelled;
            }

            try
            {
                IList<Reference> pickedReferences = uiDocument.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.PointOnElement,
                    new TunnelCenterlineSelectionFilter(document),
                    "케이블 트레이 기준으로 사용할 터널 중심선 또는 기준선을 여러 개 선택하세요.");

                if (pickedReferences == null || pickedReferences.Count == 0)
                {
                    TaskDialog.Show("Tunnel Cable Tray Placement", "선택한 선이 없습니다.");
                    return Result.Cancelled;
                }

                var centerlines = new List<Curve>();
                XYZ preferredDirection = null;
                XYZ preferredStartPoint = null;
                foreach (Reference picked in pickedReferences)
                {
                    if (preferredStartPoint == null)
                        preferredStartPoint = picked.GlobalPoint;

                    IList<Curve> pickedCurves = TunnelCableTrayPlacementService.GetCurvesFromReference(document, picked);
                    IList<Curve> centerlineCurves = TunnelCableTrayPlacementService.IsDwgReference(document, picked)
                        ? TunnelCableTrayPlacementService.CreateModelLinesFromCurves(document, pickedCurves)
                        : pickedCurves;

                    foreach (Curve centerline in centerlineCurves)
                    {
                        if (centerline == null)
                            continue;

                        centerlines.Add(centerline);
                        if (preferredDirection == null)
                            preferredDirection = TunnelCableTrayPlacementService.GetCurveDirection(centerline);
                    }
                }

                if (centerlines.Count == 0)
                {
                    TaskDialog.Show(
                        "Tunnel Cable Tray Placement",
                        "선택한 객체에서 배치 기준 Curve를 읽을 수 없습니다.\n\n"
                        + TunnelCableTrayPlacementService.DescribeReferences(document, pickedReferences));
                    return Result.Cancelled;
                }

                var previewIds = new List<ElementId>();
                PlacementSettings settings;
                bool keepPreview = false;

                using (var form = new TunnelCableTrayPlacementForm(document))
                {
                    form.PreviewPlacement = previewSettings =>
                    {
                        TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
                        previewIds = TunnelCableTrayPlacementService
                            .PreviewTrays(document, centerlines, previewSettings, preferredDirection, preferredStartPoint)
                            .ToList();
                        return previewIds.Count;
                    };

                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
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
                    TunnelCableTrayPlacementService.DeletePreviewTrays(document, previewIds);
                    placedCount = TunnelCableTrayPlacementService.PlaceTrays(document, centerlines, settings, preferredDirection, preferredStartPoint);
                }

                TaskDialog.Show(
                    "Tunnel Cable Tray Placement",
                    centerlines.Count + "개의 선택 선에 " + placedCount + "개의 케이블 트레이를 배치했습니다.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }
    }
}
