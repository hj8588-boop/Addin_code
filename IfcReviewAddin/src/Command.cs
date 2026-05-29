using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace IfcReviewAddin
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
                    message = "Open a Revit document first.";
                    TaskDialog.Show("IFC Review", message);
                    return Result.Cancelled;
                }

                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string enginePath = FindLatestEnginePath(addinFolder);
                if (!File.Exists(enginePath))
                {
                    message = "Engine DLL was not found:\n" + enginePath;
                    TaskDialog.Show("IFC Review", message);
                    return Result.Cancelled;
                }

                byte[] engineBytes = File.ReadAllBytes(enginePath);
                Assembly engineAssembly = Assembly.Load(engineBytes);
                Type entryType = engineAssembly.GetType("IfcReviewAddin.EngineEntry");
                MethodInfo showMethod = entryType == null ? null : entryType.GetMethod("Show", BindingFlags.Public | BindingFlags.Static);
                if (showMethod == null)
                {
                    message = "Engine entry point was not found.";
                    TaskDialog.Show("IFC Review", message);
                    return Result.Cancelled;
                }

                showMethod.Invoke(null, new object[] { document });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("IFC Review - Error", ex.ToString());
                return Result.Cancelled;
            }
        }

        private static string FindLatestEnginePath(string addinFolder)
        {
            string defaultPath = Path.Combine(addinFolder, "IfcReviewEngine.dll");
            string[] versioned = Directory.GetFiles(addinFolder, "IfcReviewEngine_*.dll");
            if (versioned.Length == 0)
                return defaultPath;

            Array.Sort(versioned, StringComparer.OrdinalIgnoreCase);
            return versioned[versioned.Length - 1];
        }
    }
}
