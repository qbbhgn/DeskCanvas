using DeskCanvas.Interop;
using DeskCanvas.UI;

namespace DeskCanvas.Services;

/// <summary>用低级鼠标钩子收集桌面输入，并将鼠标消息发送给命中显示器上的壁纸窗口。</summary>
internal sealed class MouseForwarder : IDisposable
{
    private readonly NativeMethods.HookProc _callback;
    private IntPtr _hook;
    private readonly DesktopEnvironmentService _desktop = new();
    private bool _toolPress;
    public MouseForwarder() { _callback = Hook; _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0); }
    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
            uint message = unchecked((uint)wParam.ToInt64());
            Point point = new(data.Pt.X, data.Pt.Y);
            bool tool = DesktopEnvironmentService.ActiveWindow(point) is not null &&
                DesktopEnvironmentService.IsDesktop(point) &&
                (NativeMethods.GetAsyncKeyState(0x11) < 0 || NativeMethods.GetAsyncKeyState(0x12) < 0);
            if (message == NativeMethods.WmLButtonUp && _toolPress)
            {
                _toolPress = false;
                WallpaperManager.ForwardMouse(data.Pt.X, data.Pt.Y, message, data.MouseData);
                return new IntPtr(1);
            }
            if (tool && message == NativeMethods.WmLButtonDown)
            {
                _toolPress = true;
                WallpaperManager.ForwardMouse(data.Pt.X, data.Pt.Y, message, data.MouseData);
                _desktop.AnimateTool();
                return new IntPtr(1);
            }
            if (DesktopEnvironmentService.ActiveWindow(point) is not null && !DesktopEnvironmentService.IsDesktop(point))
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            WallpaperManager.ForwardMouse(data.Pt.X, data.Pt.Y, message, data.MouseData);
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
    public void Dispose() { _desktop.Dispose(); if (_hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } }
}
