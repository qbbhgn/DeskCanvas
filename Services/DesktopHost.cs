using DeskCanvas.Interop;

namespace DeskCanvas.Services;

/// <summary>Locates the Explorer background layer used to host wallpaper windows.</summary>
internal static class DesktopHost
{
    private const string IconViewClass = "SHELLDLL_DefView";
    private const string WorkerClass = "WorkerW";
    private const uint SpawnWorkerMessage = 0x052C;

    /// <summary>
    /// Finds a WorkerW that does not own the desktop icon view. Newer Explorer
    /// builds keep this WorkerW as a direct child of Progman; older builds use
    /// a top-level WorkerW next to the icon host.
    /// </summary>
    public static bool TryFindBackLayer(out IntPtr host)
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            // Ask Explorer to create the background WorkerW when supported.
            NativeMethods.SendMessageTimeout(progman, SpawnWorkerMessage, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
            NativeMethods.SendMessageTimeout(progman, SpawnWorkerMessage, new IntPtr(0xD), IntPtr.Zero, 0, 1000, out _);
        }

        // Prefer the direct child. This avoids selecting unrelated top-level
        // WorkerW windows that may also exist in the shell's window tree.
        IntPtr candidate = progman == IntPtr.Zero
            ? IntPtr.Zero
            : FindWorkerWithoutIconView(progman, IntPtr.Zero);
        if (candidate != IntPtr.Zero)
        {
            host = candidate;
            return true;
        }

        // Windows 10 and older Windows 11 layouts expose the icon host and
        // background WorkerW as top-level siblings.
        NativeMethods.EnumWindows((top, _) =>
        {
            if (NativeMethods.FindWindowEx(top, IntPtr.Zero, IconViewClass, null) == IntPtr.Zero) return true;
            candidate = FindWorkerWithoutIconView(IntPtr.Zero, top);
            return candidate == IntPtr.Zero;
        }, IntPtr.Zero);

        host = candidate;
        return host != IntPtr.Zero;
    }

    /// <summary>Finds the next WorkerW under a parent that does not contain the icon view.</summary>
    private static IntPtr FindWorkerWithoutIconView(IntPtr parent, IntPtr after)
    {
        IntPtr worker = NativeMethods.FindWindowEx(parent, after, WorkerClass, null);
        while (worker != IntPtr.Zero)
        {
            if (NativeMethods.FindWindowEx(worker, IntPtr.Zero, IconViewClass, null) == IntPtr.Zero)
                return worker;
            worker = NativeMethods.FindWindowEx(parent, worker, WorkerClass, null);
        }
        return IntPtr.Zero;
    }

    /// <summary>Returns the icon view child used to keep wallpaper below desktop icons.</summary>
    public static IntPtr FindIconViewHost(IntPtr host)
    {
        return host == IntPtr.Zero ? IntPtr.Zero : NativeMethods.FindWindowEx(host, IntPtr.Zero, IconViewClass, null);
    }

    /// <summary>Finds the Explorer window that directly contains the desktop icons.</summary>
    public static bool TryFindIconView(out IntPtr iconView)
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        iconView = progman == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(progman, IntPtr.Zero, IconViewClass, null);
        if (iconView != IntPtr.Zero) return true;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            found = NativeMethods.FindWindowEx(top, IntPtr.Zero, IconViewClass, null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        iconView = found;
        return iconView != IntPtr.Zero;
    }
}
