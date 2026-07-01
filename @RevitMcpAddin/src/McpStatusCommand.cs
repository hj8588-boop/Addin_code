using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMcpAddin
{
    [Transaction(TransactionMode.ReadOnly)]
    public class McpStatusCommand : IExternalCommand
    {
        private static readonly string CommDir = @"C:\revit_mcp";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            bool dirExists = Directory.Exists(CommDir);
            bool hasCommand = File.Exists(Path.Combine(CommDir, "command.json"));
            bool hasResult = File.Exists(Path.Combine(CommDir, "result.json"));

            string status = dirExists ? "Ready" : "C:\\revit_mcp folder is missing";
            string detail =
                "Communication folder: " + CommDir + "\n" +
                "Status: " + status + "\n\n" +
                "command.json exists: " + (hasCommand ? "Yes" : "No") + "\n" +
                "result.json exists: " + (hasResult ? "Yes" : "No") + "\n\n" +
                "Send a Revit MCP command from Claude, then keep this Revit model open.";

            TaskDialog.Show("Claude MCP Listener", detail);
            return Result.Succeeded;
        }
    }
}
