using System.Text.Json;
using DeskCanvas.Interop;
using DeskCanvas.Models;
using DeskCanvas.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DeskCanvas.UI;

/// <summary>
/// 单显示器无边框壁纸窗体。窗口先以隐藏状态创建 WebView2，挂接到桌面图标视图之后才显示，
/// 因此应用壁纸时不会出现"整屏黑一下"再被桌面覆盖的闪烁。
/// </summary>
internal sealed class WallpaperWindow : Form
{
    private static readonly SemaphoreSlim WebViewInitializationGate = new(1, 1);
    private static CoreWebView2Environment? _webViewEnvironment;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private TaskCompletionSource<bool>? _closeCompletion;
    private string _folder = string.Empty;
    private WallpaperManifest? _manifest;
    private IntPtr _host;
    private bool _coverMode;
    private bool _paused;
    private bool _mounted;
    private bool _closing;
    public static List<WallpaperWindow> Instances { get; } = new();
    public Rectangle ScreenBounds { get; }

    /// <summary>窗口已挂接到桌面层级且 WebView2 可用；为 false 时应在下次应用时重建窗口。</summary>
    public bool IsMounted => _mounted && !_closing && !IsDisposed && _web.CoreWebView2 is not null;

    /// <summary>true 表示当前系统没有可供挂接的桌面背景层，壁纸以覆盖桌面的普通窗口运行。</summary>
    public bool UsesCoverWindow => _coverMode;

    public WallpaperWindow(Screen screen)
    {
        ScreenBounds = screen.Bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        SetStyle(ControlStyles.Selectable, false);
        Bounds = screen.Bounds;
        TopMost = false;
        BackColor = Color.Black;
        Controls.Add(_web);
        Instances.Add(this);
        FormClosed += (_, _) =>
        {
            Instances.Remove(this);
            _closeCompletion?.TrySetResult(true);
        };
    }

    /// <summary>壁纸不需要焦点：避免应用时抢走前台窗口。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WsExNoActivate;
            return parameters;
        }
    }

    /// <summary>把壁纸显示到本窗口对应的显示器上；窗口已挂接时只切换网页内容。</summary>
    public async Task StartAsync(string folder, WallpaperManifest manifest)
    {
        _folder = folder;
        _manifest = manifest;
        if (IsMounted)
        {
            await NavigateToEntryAsync();
            return;
        }
        IntPtr host;
        bool classicLayer = DesktopHost.TryFindBackLayer(out host);
        if (classicLayer)
        {
            _host = host;
            // 经典桌面结构：先建句柄（窗口隐藏）挂到图标视图后方，用户看不到中间状态。
            Mount(Handle, host);
        }
        // 先初始化并加载首屏，再显示窗口，避免出现黑屏闪烁。
        await EnsureCoreWebView2Async();
        await ConfigureWebViewAsync();
        await NavigateToEntryAsync();
        if (classicLayer) ShowBehindDesktopIcons();
        else ShowAsCoverWindow();
        _mounted = true;
    }

    /// <summary>窗口已挂接在桌面层之后才真正显示，因此不会出现抢占前台或桌面闪烁。</summary>
    private void ShowBehindDesktopIcons()
    {
        IntPtr hwnd = Handle;
        Visible = true;
        // WinForms 显示窗口时会把它提到 Z 序顶端，这里重新压回图标视图下方。
        if (_host != IntPtr.Zero) ApplyIconLayerPosition(hwnd, _host);
        Invalidate(true);
        Update();
    }

    /// <summary>
    /// 覆盖模式：系统桌面不再提供独立背景层时（Windows 11 25H2 起），
    /// 挂在桌面层里的窗口不会被合成显示，只能把壁纸作为普通全屏窗口压在所有窗口之下。
    /// 代价是桌面图标会被壁纸盖住。
    /// </summary>
    private void ShowAsCoverWindow()
    {
        _coverMode = true;
        IntPtr hwnd = Handle;
        Bounds = ScreenBounds;
        Visible = true;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndBottom, 0, 0, 0, 0,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize);
        Invalidate(true);
        Update();
    }

    /// <summary>覆盖模式下窗口是普通窗口，"最小化"（Win+D、显示桌面）会让壁纸消失，这里忽略该命令。</summary>
    protected override void WndProc(ref Message message)
    {
        if (_coverMode && message.Msg == NativeMethods.WmSysCommand && (message.WParam.ToInt64() & 0xFFF0) == NativeMethods.ScMinimize) return;
        base.WndProc(ref message);
    }

    /// <summary>覆盖模式下若窗口被系统最小化或隐藏，把它恢复到桌面最底层。</summary>
    public void EnsureVisible()
    {
        if (!_coverMode || !_mounted || _closing || IsDisposed || !IsHandleCreated) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (!Visible) Visible = true;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HwndBottom, 0, 0, 0, 0,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
    }

    private async Task EnsureCoreWebView2Async()
    {
        await WebViewInitializationGate.WaitAsync();
        try
        {
            await _web.EnsureCoreWebView2Async(await GetWebViewEnvironmentAsync());
        }
        finally
        {
            WebViewInitializationGate.Release();
        }
    }

    private async Task ConfigureWebViewAsync()
    {
        CoreWebView2 core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += (_, _) => { /* 网页输入桥仅供调试样例扩展使用。 */ };
        core.ProcessFailed += (_, e) => StartupLog.Write($"壁纸渲染进程异常：{e.ProcessFailedKind}");
                await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                        (() => {
                            const runtime = window.deskCanvas || {};
                            runtime.version = '1';
                            runtime.isPaused = false;
                            window.deskCanvas = runtime;
                        })();
                        """);
    }

    /// <summary>把窗口变成桌面宿主的子窗口并对齐到显示器坐标，同时保证图标视图在其上方。</summary>
    private void Mount(IntPtr hwnd, IntPtr host)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr((style & ~NativeMethods.WsPopup) | NativeMethods.WsChild));
        NativeMethods.SetParent(hwnd, host);
        NativeMethods.GetWindowRect(host, out var hostRect);
        // SetParent 后坐标属于宿主客户区，必须使用宿主窗口实际屏幕位置换算。
        NativeMethods.MoveWindow(hwnd, ScreenBounds.X - hostRect.Left, ScreenBounds.Y - hostRect.Top, ScreenBounds.Width, ScreenBounds.Height, false);
        ApplyIconLayerPosition(hwnd, host);
    }

    /// <summary>把壁纸窗口压在桌面图标视图下方：位于桌面背景之上、桌面图标之下。</summary>
    private static void ApplyIconLayerPosition(IntPtr hwnd, IntPtr host)
    {
        IntPtr iconView = DesktopHost.FindIconViewHost(host);
        NativeMethods.SetWindowPos(hwnd, iconView != IntPtr.Zero ? iconView : NativeMethods.HwndBottom, 0, 0, 0, 0,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
    }

    /// <summary>窗口已挂接在桌面层之后才真正显示，因此不会出现抢占前台或桌面闪烁。</summary>
    private void ShowInDesktop()
    {
        IntPtr hwnd = Handle;
        Visible = true;
        // WinForms 显示窗口时会把它提到 Z 序顶端，这里重新压回图标视图下方。
        if (_host != IntPtr.Zero) ApplyIconLayerPosition(hwnd, _host);
        Invalidate(true);
        Update();
    }

    private async Task NavigateToEntryAsync()
    {
        CoreWebView2? core = _web.CoreWebView2;
        if (core is null || _manifest is null) return;
        string entry = Path.GetFullPath(Path.Combine(_folder, _manifest.Entry));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => completion.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            core.Navigate(new Uri(entry).AbsoluteUri);
            // 页面加载完（或超时）后再显示窗口，避免先看到空白/黑屏。
            await Task.WhenAny(completion.Task, Task.Delay(4000));
            await InitializeRuntimeAsync();
        }
        finally
        {
            core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private async Task InitializeRuntimeAsync()
    {
        if (_manifest is null || _web.CoreWebView2 is null) return;
        string properties = JsonSerializer.Serialize(_manifest.Properties);
        string metadata = JsonSerializer.Serialize(new
        {
            _manifest.Id,
            _manifest.Title,
            _manifest.Author,
            _manifest.Version,
            _manifest.Type,
            _manifest.AudioReactive,
            _manifest.RecommendedFps
        });
        string script = "(() => {" +
            "const runtime = window.deskCanvas || (window.deskCanvas = {});" +
            "runtime.metadata = " + metadata + ";" +
            "runtime.properties = " + properties + ";" +
            "window.dispatchEvent(new CustomEvent('deskcanvas:ready', { detail: runtime }));" +
            "})();";
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write("壁纸运行时初始化失败", ex); }
    }

    public Task CloseAsync()
    {
        if (_closeCompletion is not null) return _closeCompletion.Task;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closeCompletion = completion;
        _closing = true;
        _mounted = false;
        if (IsDisposed || !IsHandleCreated)
        {
            Instances.Remove(this);
            completion.TrySetResult(true);
            return completion.Task;
        }
        try
        {
            // 窗口已被 SetParent 到桌面层级，Form.Close() 在这种状态下不一定触发 FormClosed，
            // 直接隐藏并销毁句柄更可靠。
            NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
            _web.Dispose();
            NativeMethods.DestroyWindow(Handle);
        }
        catch (Exception ex)
        {
            StartupLog.Write("关闭壁纸窗口失败", ex);
        }
        finally
        {
            Instances.Remove(this);
            try { if (!IsDisposed) Dispose(); } catch (Exception ex) { StartupLog.Write("释放壁纸窗口失败", ex); }
            completion.TrySetResult(true);
        }
        return completion.Task;
    }

    private static Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskCanvas", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }

    private static async Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
    {
        if (_webViewEnvironment is not null) return _webViewEnvironment;
        CoreWebView2Environment environment = await CreateWebViewEnvironmentAsync();
        environment.BrowserProcessExited += (_, _) =>
        {
            // 浏览器进程退出后缓存的 Environment 不再可用，下次应用时重建。
            _webViewEnvironment = null;
            foreach (WallpaperWindow window in Instances.ToArray()) window._mounted = false;
        };
        _webViewEnvironment = environment;
        return environment;
    }

    /// <summary>丢弃缓存的 WebView2 环境；运行时被更新或浏览器进程退出后必须重建。</summary>
    public static void ResetEnvironment() => _webViewEnvironment = null;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (_web.CoreWebView2 is null) return;
        _web.CoreWebView2.IsMuted = paused;
        _ = ExecutePauseScriptAsync(paused);
    }

    private async Task ExecutePauseScriptAsync(bool paused)
    {
        if (_web.CoreWebView2 is null) return;
        string state = paused ? "pause" : "resume";
        string pausedValue = paused ? "true" : "false";
                string script = "(() => {" +
                        "const paused = " + pausedValue + ";" +
                        "const runtime = window.deskCanvas || (window.deskCanvas = {});" +
                        "runtime.isPaused = paused;" +
                        "window.dispatchEvent(new CustomEvent('deskcanvas:" + state + "', { detail: { source: 'host' } }));" +
                        "document.querySelectorAll('video, audio').forEach(media => {" +
                        "if (paused) media.pause();" +
                        "else if (media.dataset.deskCanvasAutoplay === 'true' || !media.paused) media.play().catch(() => {});" +
                        "});" +
                        "})();";
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write($"壁纸{state}协议执行失败", ex); }
    }

    public void ForwardMouse(int x, int y, uint message)
    {
        if (_paused || _web.CoreWebView2 is null) return;
        if (!ScreenBounds.Contains(x, y)) return;
        int localX = x - ScreenBounds.Left;
        int localY = y - ScreenBounds.Top;
        int cssWidth = Math.Max(1, _web.ClientSize.Width);
        int cssHeight = Math.Max(1, _web.ClientSize.Height);
        int localWidth = Math.Max(1, ScreenBounds.Width);
        int localHeight = Math.Max(1, ScreenBounds.Height);
        int clientX = (int)Math.Round((double)localX * cssWidth / localWidth);
        int clientY = (int)Math.Round((double)localY * cssHeight / localHeight);
        string type = message switch { NativeMethods.WmMouseMove => "mousemove", NativeMethods.WmLButtonDown => "mousedown", NativeMethods.WmLButtonUp => "mouseup", NativeMethods.WmRButtonDown => "contextmenu", _ => "" };
        if (type.Length == 0) return;
        string script = $"window.dispatchEvent(new MouseEvent({JsonSerializer.Serialize(type)},{{clientX:{clientX},clientY:{clientY},bubbles:true,button:{(message is NativeMethods.WmRButtonDown ? 2 : 0)}}}))";
        _ = _web.CoreWebView2.ExecuteScriptAsync(script);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mounted = false;
            Instances.Remove(this);
            try { _web.Dispose(); } catch { /* 释放失败不影响进程退出 */ }
        }
        base.Dispose(disposing);
    }
}
