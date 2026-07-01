using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SharedParameterValuesExportAddin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return RunEngine("RunExport", commandData.Application, ref message);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                message = inner.Message;
                TaskDialog.Show("Shared Parameter Export - Error", inner.ToString());
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Shared Parameter Export - Error", ex.ToString());
                return Result.Cancelled;
            }
        }

        private static Result RunEngine(string methodName, UIApplication application, ref string message)
        {
            string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string enginePath = Path.Combine(addinFolder, "SharedParameterValuesExportEngine.dll");
            if (!File.Exists(enginePath))
            {
                message = "Engine DLL not found:\n" + enginePath;
                TaskDialog.Show("Shared Parameter Export", message);
                return Result.Cancelled;
            }

            Assembly engineAssembly = Assembly.Load(File.ReadAllBytes(enginePath));
            Type entryType = engineAssembly.GetType("SharedParameterValuesExportAddin.EngineEntry");
            MethodInfo runMethod = entryType == null
                ? null
                : entryType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

            if (runMethod == null)
            {
                message = "EngineEntry." + methodName + " was not found.";
                TaskDialog.Show("Shared Parameter Export", message);
                return Result.Cancelled;
            }

            object result = runMethod.Invoke(null, new object[] { application });
            return result is Result ? (Result)result : Result.Succeeded;
        }
    }
}
