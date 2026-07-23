using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace TunnelCableTrayPlacementAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "Codex Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = GetOrCreatePanel(application, tabName, "Tunnel");
            var button = new PushButtonData(
                "TunnelCableTrayPlacement",
                "Tunnel\nTray",
                Assembly.GetExecutingAssembly().Location,
                typeof(Command).FullName);

            button.ToolTip = "Place cable trays along selected tunnel centerlines.";
            button.LargeImage = CreateIcon();
            panel.AddItem(button);

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
                    return panel;
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static System.Windows.Media.ImageSource CreateIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(28, 94, 83),
                    Color.FromArgb(8, 47, 73),
                    LinearGradientMode.ForwardDiagonal))
                {
                    FillRoundedRect(g, bg, new Rectangle(0, 0, 32, 32), 6);
                }

                using (var tunnelPen = new Pen(Color.FromArgb(230, 255, 255, 255), 2.2f))
                    g.DrawArc(tunnelPen, 5, 8, 22, 22, 200, 140);

                using (var trayPen = new Pen(Color.FromArgb(255, 125, 211, 252), 2.2f))
                {
                    g.DrawLine(trayPen, 8, 18, 24, 18);
                    g.DrawLine(trayPen, 8, 22, 24, 22);
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
