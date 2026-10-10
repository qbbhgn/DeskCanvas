using System.Windows.Automation;
using DeskCanvas.Interop;
using DeskCanvas.UI;

namespace DeskCanvas.Services;

internal sealed class DesktopEnvironmentService : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 80 };
    private readonly DesktopToolWindow _tool = new();
    private Rectangle[] _icons = [];
    private DateTime _nextRefresh;
    private bool _reading;
    public DesktopEnvironmentService()
    {
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    public static bool IsDesktop(Point point)
    {
        IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = point.X, Y = point.Y });
        IntPtr root = NativeMethods.GetAncestor(hwnd, 2);
        var name = new System.Text.StringBuilder(128);
        NativeMethods.GetClassName(root, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" ||
            WallpaperWindow.Instances.Any(window => window.IsMounted && window.Handle == root);
    }

    public static WallpaperWindow? ActiveWindow(Point point) => WallpaperWindow.Instances
        .FirstOrDefault(window => window.DesktopEnvironmentEnabled && window.ScreenBounds.Contains(point));

    private async void Update()
    {
        Point point = Cursor.Position;
        WallpaperWindow? active = ActiveWindow(point);
        bool desktop = active is not null && IsDesktop(point);
        bool ctrl = desktop && NativeMethods.GetAsyncKeyState(0x11) < 0;
        bool alt = desktop && NativeMethods.GetAsyncKeyState(0x12) < 0;
        _tool.UpdateTool(point, ctrl ? "sweep" : alt ? "swat" : "");
        foreach (WallpaperWindow window in WallpaperWindow.Instances.ToArray())
        {
            if (!window.DesktopEnvironmentEnabled) continue;
            Rectangle bounds = window.ScreenBounds;
            window.PushDesktopEnvironment(new
            {
                AltKey = desktop && active == window && alt,
                CtrlKey = desktop && active == window && ctrl,
                XRatio = (double)(point.X - bounds.X) / bounds.Width,
                YRatio = (double)(point.Y - bounds.Y) / bounds.Height,
                Icons = _icons.Where(icon => bounds.IntersectsWith(icon)).Select(icon => new
                {
                    X = (double)(icon.X - bounds.X) / bounds.Width,
                    Y = (double)(icon.Y - bounds.Y) / bounds.Height,
                    Width = (double)icon.Width / bounds.Width,
                    Height = (double)icon.Height / bounds.Height
                }).ToArray()
            });
        }
        if (_reading || DateTime.UtcNow < _nextRefresh || !WallpaperWindow.Instances.Any(window => window.DesktopEnvironmentEnabled)) return;
        _nextRefresh = DateTime.UtcNow.AddSeconds(2);
        _reading = true;
        try { _icons = await Task.Run(ReadIcons); }
        catch (Exception ex) { StartupLog.Write("Desktop icon query failed", ex); }
        finally { _reading = false; }
    }

    private static Rectangle[] ReadIcons()
    {
        IntPtr view = IntPtr.Zero;
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        view = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                view = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                return view == IntPtr.Zero;
            }, IntPtr.Zero);
        if (view == IntPtr.Zero || !NativeMethods.IsWindowVisible(view)) return [];
        AutomationElement root = AutomationElement.FromHandle(view);
        AutomationElementCollection items = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var result = new List<Rectangle>();
        foreach (AutomationElement item in items)
        {
            System.Windows.Rect rect = item.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                result.Add(new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
        }
        return result.ToArray();
    }

    public void AnimateTool() => _tool.Animate();
    public void Dispose() { _timer.Dispose(); _tool.Dispose(); }
}