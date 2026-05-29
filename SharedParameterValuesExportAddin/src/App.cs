using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace SharedParameterValuesExportAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "Codex Tools";
            const string panelName = "Parameters";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // The tab may already exist when several local add-ins share it.
            }

            RibbonPanel panel = GetOrCreatePanel(application, tabName, panelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var exportButtonData = new PushButtonData(
                "SharedParameterValuesExport",
                "Shared Parameter\nExport",
                assemblyPath,
                typeof(Command).FullName);

            exportButtonData.ToolTip = "Export shared parameter values from selected Revit categories to an Excel file.";
            exportButtonData.LargeImage = CreateParameterIcon(true);

            var importButtonData = new PushButtonData(
                "SharedParameterValuesImport",
                "Shared Parameter\nImport",
                assemblyPath,
                typeof(ImportCommand).FullName);

            importButtonData.ToolTip = "Import shared parameter values from a CSV or Excel file into existing Revit elements.";
            importButtonData.LargeImage = CreateParameterIcon(false);

            panel.AddItem(exportButtonData);
            panel.AddItem(importButtonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (string.Equals(panel.Name, panelName, StringComparison.OrdinalIgnoreCase))
                {
                    return panel;
                }
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static System.Windows.Media.ImageSource CreateParameterIcon(bool exportMode)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Color top = exportMode ? Color.FromArgb(25, 113, 185) : Color.FromArgb(33, 132, 82);
                Color bottom = exportMode ? Color.FromArgb(20, 61, 125) : Color.FromArgb(18, 91, 70);
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), top, bottom, LinearGradientMode.ForwardDiagonal))
                    FillRoundedRect(g, bg, new Rectangle(0, 0, 32, 32), 6);

                using (var sheetBrush = new SolidBrush(Color.FromArgb(245, 250, 255)))
                using (var foldBrush = new SolidBrush(Color.FromArgb(205, 225, 240)))
                using (var textPen = new Pen(Color.FromArgb(70, 100, 130), 1.2f))
                {
                    FillRoundedRect(g, sheetBrush, new Rectangle(7, 5, 14, 21), 2);
                    g.FillPolygon(foldBrush, new[] { new Point(17, 5), new Point(21, 9), new Point(17, 9) });
                    g.DrawLine(textPen, 10, 13, 18, 13);
                    g.DrawLine(textPen, 10, 17, 18, 17);
                    g.DrawLine(textPen, 10, 21, 16, 21);
                }

                using (var arrowBrush = new SolidBrush(Color.White))
                using (var arrowPen = new Pen(Color.White, 3.0f))
                {
                    if (exportMode)
                    {
                        g.DrawLine(arrowPen, 21, 16, 27, 16);
                        g.FillPolygon(arrowBrush, new[] { new Point(27, 16), new Point(22, 12), new Point(22, 20) });
                    }
                    else
                    {
                        g.DrawLine(arrowPen, 27, 16, 21, 16);
                        g.FillPolygon(arrowBrush, new[] { new Point(20, 16), new Point(25, 12), new Point(25, 20) });
                    }
                }

                return ToBitmapSource(bmp);
            }
        }

        private static void FillRoundedRect(Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            int d = radius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private static System.Windows.Media.ImageSource ToBitmapSource(Bitmap bmp)
        {
            IntPtr handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
