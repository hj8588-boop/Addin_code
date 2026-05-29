using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace LightingCalculationAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "Codex Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            RibbonPanel lightingPanel = GetOrCreatePanel(application, tabName, "Lighting");
            var lightingButton = new PushButtonData(
                "LightingCalculation",
                "Lighting\nCalc",
                assemblyPath,
                typeof(Command).FullName);

            lightingButton.ToolTip = "Calculate and export lighting requirements from Revit spaces and fixtures.";
            lightingButton.LargeImage = CreateLightingIcon();
            lightingPanel.AddItem(lightingButton);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static System.Windows.Media.ImageSource CreateLightingIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(36, 116, 185),
                    Color.FromArgb(16, 60, 120),
                    LinearGradientMode.ForwardDiagonal))
                {
                    FillRoundedRect(g, bg, new Rectangle(0, 0, 32, 32), 6);
                }

                using (var glowBrush = new SolidBrush(Color.FromArgb(80, 255, 240, 120)))
                    g.FillEllipse(glowBrush, 5, 2, 22, 20);

                using (var bulbBrush = new SolidBrush(Color.FromArgb(255, 235, 100)))
                    g.FillEllipse(bulbBrush, 9, 5, 14, 14);

                using (var pen = new Pen(Color.White, 2f))
                {
                    g.DrawLine(pen, 13, 19, 13, 23);
                    g.DrawLine(pen, 19, 19, 19, 23);
                    g.DrawLine(pen, 11, 23, 21, 23);
                    g.DrawLine(pen, 12, 26, 20, 26);
                }

                using (var rayPen = new Pen(Color.FromArgb(200, 255, 255, 180), 1.5f))
                {
                    g.DrawLine(rayPen, 16, 2, 16, 0);
                    g.DrawLine(rayPen, 6, 6, 4, 4);
                    g.DrawLine(rayPen, 26, 6, 28, 4);
                    g.DrawLine(rayPen, 4, 14, 2, 14);
                    g.DrawLine(rayPen, 28, 14, 30, 14);
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

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (string.Equals(panel.Name, panelName, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }
    }
}
