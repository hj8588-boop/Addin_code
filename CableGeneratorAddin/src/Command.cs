using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CableGeneratorAddin
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
                    message = "Open a Revit document first.";
                    TaskDialog.Show("Cable Generator", message);
                    return Result.Cancelled;
                }

                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string enginePath = Path.Combine(addinFolder, "CableGeneratorEngine.dll");
                if (!File.Exists(enginePath))
                {
                    message = "Cannot find engine DLL:\n" + enginePath;
                    TaskDialog.Show("Cable Generator", message);
                    return Result.Cancelled;
                }

                Assembly engineAssembly = Assembly.Load(File.ReadAllBytes(enginePath));
                Type entryType = engineAssembly.GetType("CableGeneratorAddin.EngineEntry");
                MethodInfo runMethod = entryType == null
                    ? null
                    : entryType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                if (runMethod == null)
                {
                    message = "Cannot find EngineEntry.Run.";
                    TaskDialog.Show("Cable Generator", message);
                    return Result.Cancelled;
                }

                object result = runMethod.Invoke(null, new object[] { commandData.Application });
                return result is Result ? (Result)result : Result.Succeeded;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                message = inner.Message;
                TaskDialog.Show("Cable Generator - Error", inner.ToString());
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Cable Generator - Error", ex.ToString());
                return Result.Cancelled;
            }
        }
    }
}
