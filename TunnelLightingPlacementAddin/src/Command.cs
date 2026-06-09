using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TunnelLightingPlacementAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDocument = commandData.Application.ActiveUIDocument;
                if (uiDocument == null || uiDocument.Document == null)
                {
                    message = "Revit 문서를 먼저 열어주세요.";
                    TaskDialog.Show("터널 전등 자동배치", message);
                    return Result.Cancelled;
                }

                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string enginePath = Path.Combine(addinFolder, "TunnelLightingPlacementEngine.dll");
                if (!File.Exists(enginePath))
                {
                    message = "Engine DLL을 찾을 수 없습니다:\n" + enginePath;
                    TaskDialog.Show("터널 전등 자동배치", message);
                    return Result.Cancelled;
                }

                Assembly engineAssembly = Assembly.Load(File.ReadAllBytes(enginePath));
                Type entryType = engineAssembly.GetType("TunnelLightingPlacementAddin.EngineEntry");
                MethodInfo runMethod = entryType == null
                    ? null
                    : entryType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                if (runMethod == null)
                {
                    message = "EngineEntry.Run 진입점을 찾을 수 없습니다.";
                    TaskDialog.Show("터널 전등 자동배치", message);
                    return Result.Cancelled;
                }

                object result = runMethod.Invoke(null, new object[] { commandData.Application });
                return result is Result ? (Result)result : Result.Succeeded;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                message = inner.Message;
                TaskDialog.Show("터널 전등 자동배치 - 오류", inner.ToString());
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("터널 전등 자동배치 - 오류", ex.ToString());
                return Result.Cancelled;
            }
        }
    }
}
