using DeskCanvas.Services;

namespace DeskCanvas.UI;

/// <summary>隐藏主窗体的托盘应用上下文；关闭设置只隐藏窗口，不结束程序。</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly WallpaperManager _manager;
    private readonly NotifyIcon _tray;
    private readonly SettingsForm _settings;
    private readonly EventWaitHandle _showSettings;
    private readonly RegisteredWaitHandle _showSettingsRegistration;
    private readonly System.Windows.Forms.Timer _fullscreenTimer = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _playlistTimer = new() { Interval = 15000 };
    private bool _playlistRotationInProgress;
    private bool _pausedByFullscreen;
    public TrayApplicationContext(WallpaperManager manager, EventWaitHandle showSettings)
    {
        _manager = manager;
        _showSettings = showSettings;
        _settings = new SettingsForm(manager);
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("暂停/恢复壁纸", null, (_, _) => _manager.Pause(!_manager.IsPaused));
        menu.Items.Add("关闭壁纸", null, (_, _) => { _manager.CloseWallpapers(); _settings.RefreshRuntimeState(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => Exit());
        _tray = new NotifyIcon { Icon = Program.AppIcon, Text = "DeskCanvas 交互式壁纸", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _settings.FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _settings.Hide(); } };
        _fullscreenTimer.Tick += (_, _) => { _manager.EnsureVisible(); if (_manager.AutoPause) PauseIfFullscreen(); };
        _playlistTimer.Tick += async (_, _) => await RotatePlaylistIfDueAsync();
        _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(_showSettings, (_, _) =>
        {
            if (!_settings.IsDisposed) _settings.BeginInvoke(ShowSettings);
        }, null, Timeout.Infinite, false);
        _fullscreenTimer.Start();
        _playlistTimer.Start();
        ShowSettings();
    }
    private void ShowSettings() { if (_settings.IsDisposed) return; _settings.Show(); _settings.WindowState = FormWindowState.Normal; _settings.Activate(); }
    private async Task RotatePlaylistIfDueAsync()
    {
        if (_playlistRotationInProgress || _manager.IsPaused || !_manager.PlaylistEnabled) return;
        _playlistRotationInProgress = true;
        try { if (await _manager.RotatePlaylistAsync()) _settings.RefreshRuntimeState(); }
        catch (Exception ex) { StartupLog.Write("澹佺焊鎾斁鍒楄〃杞崲澶辫触", ex); }
        finally { _playlistRotationInProgress = false; }
    }
    /// <summary>只在"全屏 / 非全屏"状态发生变化时切换壁纸，避免每 1.5 秒重复执行脚本。</summary>
    private void PauseIfFullscreen()
    {
        bool fullscreen = IsFullscreenForeground();
        if (fullscreen == _pausedByFullscreen) return;
        _pausedByFullscreen = fullscreen;
        _manager.Pause(fullscreen);
        StartupLog.Write(fullscreen ? "检测到全屏应用，已暂停壁纸渲染" : "前台已回到桌面，已恢复壁纸渲染");
    }
    private static bool IsFullscreenForeground()
    {
        IntPtr hwnd = Interop.NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        Interop.NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        // 桌面、任务栏、输入法等外壳窗口同样铺满屏幕，但不是全屏应用。
        if (DesktopBackground.IsShellWindow(hwnd)) return false;
        if (!Interop.NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        return Screen.AllScreens.Any(s => Math.Abs(s.Bounds.Left - rect.Left) <= 2 && Math.Abs(s.Bounds.Top - rect.Top) <= 2 && Math.Abs(s.Bounds.Right - rect.Right) <= 2 && Math.Abs(s.Bounds.Bottom - rect.Bottom) <= 2);
    }
    private void Exit()
    {
        _fullscreenTimer.Stop(); _fullscreenTimer.Dispose(); _playlistTimer.Stop(); _playlistTimer.Dispose(); _tray.Visible = false; _tray.Dispose();
        _showSettingsRegistration.Unregister(null); _showSettings.Dispose();
        _settings.FormClosing -= (_, _) => { };
        _manager.Dispose(); _settings.Dispose(); ExitThread();
    }
    protected override void Dispose(bool disposing) { if (disposing) { _showSettingsRegistration.Unregister(null); _showSettings.Dispose(); _manager.Dispose(); _tray.Dispose(); _fullscreenTimer.Dispose(); _playlistTimer.Dispose(); _settings.Dispose(); } base.Dispose(disposing); }
}
