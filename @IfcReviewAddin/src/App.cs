using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace IfcReviewAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "Codex Tools";
            try { application.CreateRibbonTab(tabName); } catch { }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel panel = GetOrCreatePanel(application, tabName, "IFC Review");

            var button = new PushButtonData(
                "IfcReview",
                "IFC\nReview",
                assemblyPath,
                typeof(Command).FullName);

            button.ToolTip = "Review IFC export targets and export detailed family, type, and instance parameter data.";
            button.LargeImage = CreateIfcIcon();
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

        private static System.Windows.Media.ImageSource CreateIfcIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(20, 105, 92),
                    Color.FromArgb(30, 62, 110),
                    LinearGradientMode.ForwardDiagonal))
                {
                    FillRoundedRect(g, bg, new Rectangle(0, 0, 32, 32), 6);
                }

                using (var face = new SolidBrush(Color.FromArgb(235, 248, 255)))
                using (var shade = new SolidBrush(Color.FromArgb(120, 194, 220)))
                using (var pen = new Pen(Color.White, 1.4f))
                {
                    Point[] top = { new Point(16, 5), new Point(25, 10), new Point(16, 15), new Point(7, 10) };
                    Point[] left = { new Point(7, 10), new Point(16, 15), new Point(16, 26), new Point(7, 20) };
                    Point[] right = { new Point(25, 10), new Point(16, 15), new Point(16, 26), new Point(25, 20) };
                    g.FillPolygon(face, top);
                    g.FillPolygon(shade, left);
                    g.FillPolygon(new SolidBrush(Color.FromArgb(70, 160, 185)), right);
                    g.DrawPolygon(pen, top);
                    g.DrawPolygon(pen, left);
                    g.DrawPolygon(pen, right);
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
