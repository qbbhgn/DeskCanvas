using DeskCanvas.Interop;
using DeskCanvas.UI;

namespace DeskCanvas.Services;

/// <summary>用低级鼠标钩子收集桌面输入，并将鼠标消息发送给命中显示器上的壁纸窗口。</summary>
internal sealed class MouseForwarder : IDisposable
{
    private readonly NativeMethods.HookProc _callback;
    private IntPtr _hook;
    public MouseForwarder() { _callback = Hook; _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0); }
    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
            uint message = unchecked((uint)wParam.ToInt64());
            WallpaperManager.ForwardMouse(data.Pt.X, data.Pt.Y, message, data.MouseData);
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
    public void Dispose() { if (_hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } }
}
