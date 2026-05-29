using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace LightingCalculationAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDocument = commandData.Application.ActiveUIDocument;
                Document document = uiDocument == null ? null : uiDocument.Document;
                if (document == null)
                {
                    message = "Revit 문서를 먼저 열어주세요.";
                    TaskDialog.Show("조도 계산서", message);
                    return Result.Cancelled;
                }

                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string enginePath = Path.Combine(addinFolder, "LightingCalculationEngine.dll");
                if (!File.Exists(enginePath))
                {
                    message = "계산 엔진 DLL을 찾을 수 없습니다:\n" + enginePath;
                    TaskDialog.Show("조도 계산서", message);
                    return Result.Cancelled;
                }

                byte[] engineBytes = File.ReadAllBytes(enginePath);
                Assembly engineAssembly = Assembly.Load(engineBytes);
                Type entryType = engineAssembly.GetType("LightingCalculationAddin.EngineEntry");
                MethodInfo showMethod = entryType == null ? null : entryType.GetMethod("Show", BindingFlags.Public | BindingFlags.Static);
                if (showMethod == null)
                {
                    message = "계산 엔진 진입점을 찾을 수 없습니다.";
                    TaskDialog.Show("조도 계산서", message);
                    return Result.Cancelled;
                }

                showMethod.Invoke(null, new object[] { document });

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("조도 계산서 - Error", ex.ToString());
                return Result.Cancelled;
            }
        }
    }
}
