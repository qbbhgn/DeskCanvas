using System.Text;
using DeskCanvas.Interop;

namespace DeskCanvas.Services;

/// <summary>
/// 桌面外壳窗口识别。桌面、任务栏、输入法等系统窗口同样铺满整个屏幕，
/// 但它们不是"全屏应用"，自动暂停壁纸时必须排除，否则壁纸会被误暂停。
/// </summary>
internal static class DesktopBackground
{
    private static readonly string[] ShellWindowClasses =
    {
        "Progman", "WorkerW", "SHELLDLL_DefView", "SysListView32",
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "XamlExplorerHostIslandWindow"
    };

    /// <summary>
    /// 判断前台窗口是否只是桌面/系统外壳。这些窗口同样铺满整个屏幕，但并不是"全屏应用"，
    /// 不能据此暂停壁纸，否则壁纸刚应用就会被自动暂停隐藏。
    /// </summary>
    public static bool IsShellWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        // DWM 标记为 cloaked 的窗口虽然铺满屏幕，实际并不显示。
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
        var buffer = new StringBuilder(64);
        if (NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) <= 0) return false;
        string name = buffer.ToString();
        foreach (string item in ShellWindowClasses)
        {
            if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
