using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace RevitSheetBatchEditor.Loader
{
    public sealed class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                return StartRibbon(application);
            }
            catch (Exception ex)
            {
                TryWriteStartupLog(ex);
                return Result.Failed;
            }
        }

        private static Result StartRibbon(UIControlledApplication application)
        {
            const string tabName = "Codex Tools";
            // Codex Tools may already have been created by another add-in.
            // Revit throws Autodesk.Revit.Exceptions.ArgumentException here,
            // which is different from System.ArgumentException.
            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = GetOrCreatePanel(application, tabName, "Drawing Automation");
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            var button = new PushButtonData(
                "SheetBatchEditor",
                "Sheet Batch\nEditor",
                assemblyPath,
                typeof(SheetBatchCommand).FullName);

            if (HasRibbonItem(panel, "SheetBatchEditor")) return Result.Succeeded;

            PushButton pushButton = panel.AddItem(button) as PushButton;
            if (pushButton != null)
            {
                pushButton.ToolTip = "Edit sheet numbers, sheet names, and sheet/title block parameters in one place.";
                pushButton.LongDescription = "Validates changes before applying them and safely handles number conflicts with temporary sheet numbers.";
                pushButton.LargeImage = LoadRibbonImage("SheetBatchEditorIcon32.png");
                pushButton.Image = LoadRibbonImage("SheetBatchEditorIcon16.png");
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) { return Result.Succeeded; }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (string.Equals(panel.Name, panelName, StringComparison.OrdinalIgnoreCase)) return panel;
            }
            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static bool HasRibbonItem(RibbonPanel panel, string itemName)
        {
            foreach (RibbonItem item in panel.GetItems())
            {
                if (string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static ImageSource LoadRibbonImage(string fileName)
        {
            try
            {
                string resourceName = "RevitSheetBatchEditor.Loader.Resources." + fileName;
                using (Stream stream = typeof(App).Assembly.GetManifestResourceStream(resourceName))
                using (var bitmap = stream == null ? null : new Bitmap(stream))
                {
                    if (bitmap == null) return null;
                    IntPtr handle = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
                    try
                    {
                        ImageSource image = Imaging.CreateBitmapSourceFromHBitmap(
                            handle,
                            IntPtr.Zero,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        image.Freeze();
                        return image;
                    }
                    finally
                    {
                        DeleteObject(handle);
                    }
                }
            }
            catch { return null; }
        }

        private static void TryWriteStartupLog(Exception exception)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "RevitSheetBatchEditor-startup.log");
                File.WriteAllText(path, DateTime.Now.ToString("O") + Environment.NewLine + exception);
            }
            catch { }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SheetBatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                string enginePath = ResolveEnginePath();
                if (!File.Exists(enginePath))
                {
                    message = "Engine DLL not found: " + enginePath;
                    return Result.Failed;
                }

                // 바이트로 읽어 로드하면 Engine DLL 파일이 잠기지 않아 개발 중 교체하기 쉽습니다.
                Assembly engine = Assembly.Load(File.ReadAllBytes(enginePath));
                Type entryType = engine.GetType("RevitSheetBatchEditor.Engine.EntryPoint", true);
                MethodInfo runMethod = entryType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                object result = runMethod.Invoke(null, new object[] { commandData });
                return result is Result ? (Result)result : Result.Succeeded;
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                message = cause.Message;
                TaskDialog.Show("Sheet Batch Editor", cause.ToString());
                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Sheet Batch Editor", ex.ToString());
                return Result.Failed;
            }
        }

        private static string ResolveEnginePath()
        {
            const string engineFileName = "RevitSheetBatchEditor.Engine.dll";

            // 다른 PC나 폴더를 사용할 때 환경변수로 DLL 또는 폴더 경로를 지정할 수 있습니다.
            string configuredPath = Environment.GetEnvironmentVariable("REVIT_SHEET_BATCH_ENGINE_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string configuredDll = Directory.Exists(configuredPath)
                    ? Path.Combine(configuredPath, engineFileName)
                    : configuredPath;
                if (File.Exists(configuredDll)) return configuredDll;
            }

            // 개발 중에는 빌드 결과를 직접 읽으므로 ProgramData 복사와 관리자 권한이 필요 없습니다.
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string developmentDll = Path.Combine(desktop, "codex", "RevitSheetBatchEditor", "output", engineFileName);
            if (File.Exists(developmentDll)) return developmentDll;

            // 개발 파일이 없는 배포 PC에서는 Loader와 함께 설치된 Engine을 사용합니다.
            string installedFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(installedFolder, engineFileName);
        }
    }
}
