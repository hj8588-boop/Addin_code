using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitMcpAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RevitMcpListener.Initialize(application);

            const string tabName = "Codex Tools";
            try { application.CreateRibbonTab(tabName); } catch { }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel panel = GetOrCreatePanel(application, tabName, "Claude MCP");

            var button = new PushButtonData(
                "RevitMcpStatus",
                "Claude\nMCP",
                assemblyPath,
                typeof(McpStatusCommand).FullName);

            button.ToolTip = "Claude MCP listener status. The listener starts automatically when a Revit document opens.";
            button.LargeImage = CreateMcpIcon();

            if (!HasRibbonItem(panel, "RevitMcpStatus"))
                panel.AddItem(button);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            RevitMcpListener.Shutdown();
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

        private static bool HasRibbonItem(RibbonPanel panel, string itemName)
        {
            foreach (RibbonItem item in panel.GetItems())
            {
                if (string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static System.Windows.Media.ImageSource CreateMcpIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(83, 88, 219),
                    Color.FromArgb(19, 120, 110),
                    LinearGradientMode.ForwardDiagonal))
                {
                    FillRoundedRect(g, bg, new Rectangle(0, 0, 32, 32), 6);
                }

                using (var linePen = new Pen(Color.FromArgb(180, 255, 255, 255), 1.5f))
                {
                    g.DrawLine(linePen, 16, 16, 7, 8);
                    g.DrawLine(linePen, 16, 16, 25, 8);
                    g.DrawLine(linePen, 16, 16, 7, 24);
                    g.DrawLine(linePen, 16, 16, 25, 24);
                }

                using (var centerBrush = new SolidBrush(Color.White))
                    g.FillEllipse(centerBrush, 12, 12, 8, 8);

                using (var nodeBrush = new SolidBrush(Color.FromArgb(230, 215, 245, 255)))
                {
                    g.FillEllipse(nodeBrush, 3, 4, 8, 8);
                    g.FillEllipse(nodeBrush, 21, 4, 8, 8);
                    g.FillEllipse(nodeBrush, 3, 20, 8, 8);
                    g.FillEllipse(nodeBrush, 21, 20, 8, 8);
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
