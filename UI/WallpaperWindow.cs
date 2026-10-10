using System.Text.Json;
using System.Collections.ObjectModel;
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
    private static readonly JsonSerializerOptions RuntimeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly SemaphoreSlim WebViewInitializationGate = new(1, 1);
    private static CoreWebView2Environment? _webViewEnvironment;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private TaskCompletionSource<bool>? _closeCompletion;
    private string _folder = string.Empty;
    private WallpaperManifest? _manifest;
    private IReadOnlyDictionary<string, JsonElement> _propertyValues = new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));
    private IntPtr _host;
    private bool _coverMode;
    private bool _paused;
    private bool _mounted;
    private bool _closing;
    public static List<WallpaperWindow> Instances { get; } = new();
    public Rectangle ScreenBounds { get; }
    public string ScreenDeviceName { get; }
    public string WallpaperFolder => _folder;

    /// <summary>窗口已挂接到桌面层级且 WebView2 可用；为 false 时应在下次应用时重建窗口。</summary>
    public bool IsMounted => _mounted && !_closing && !IsDisposed && _web.CoreWebView2 is not null;

    /// <summary>true 表示当前系统没有可供挂接的桌面背景层，壁纸以覆盖桌面的普通窗口运行。</summary>
    public bool UsesCoverWindow => _coverMode;
    public bool AudioReactiveEnabled => _manifest?.AudioReactive == true;
    public bool SupportsProperty(string key) =>
        !string.IsNullOrWhiteSpace(key) && _manifest?.Properties.ContainsKey(key) == true;

    public WallpaperWindow(Screen screen)
    {
        ScreenBounds = screen.Bounds;
        ScreenDeviceName = screen.DeviceName;
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
    public Task StartAsync(string folder, WallpaperManifest manifest)
        => StartAsync(folder, manifest, null);

    public async Task StartAsync(string folder, WallpaperManifest manifest, IReadOnlyDictionary<string, JsonElement>? propertyValues)
    {
        _folder = folder;
        _manifest = manifest;
        _propertyValues = new ReadOnlyDictionary<string, JsonElement>(
            propertyValues is null
                ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(propertyValues, StringComparer.OrdinalIgnoreCase));
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
        core.SetVirtualHostNameToFolderMapping("wallpaper.deskcanvas", Path.GetFullPath(_folder), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += (_, e) => _ = HandleWebMessageAsync(e);
        core.ProcessFailed += (_, e) => StartupLog.Write($"壁纸渲染进程异常：{e.ProcessFailedKind}");
                await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                        (() => {
                            const runtime = window.deskCanvas || {};
                            runtime.version = '1';
                            runtime.isPaused = false;
                            window.deskCanvas = runtime;
                        })();
                        """);
        int maxFps = Math.Clamp(_manifest?.RecommendedFps ?? 60, 1, 240);
        string fps = JsonSerializer.Serialize(maxFps);
        await core.AddScriptToExecuteOnDocumentCreatedAsync("(() => {" +
            "const runtime = window.deskCanvas || (window.deskCanvas = {});" +
            "let maxFps = " + fps + "; runtime.maxFps = maxFps;" +
            "if (runtime.__frameLimiterInstalled) return;" +
            "const nativeRaf = window.requestAnimationFrame.bind(window);" +
            "const nativeCancel = window.cancelAnimationFrame.bind(window);" +
            "let interval = 1000 / maxFps; let lastFrame = -Infinity; let nextId = 1; const pending = new Map();" +
            "runtime.setFrameRate = value => { maxFps = Math.max(1, Math.min(240, Number(value) || 60)); interval = 1000 / maxFps; runtime.maxFps = maxFps; };" +
            "const wait = id => nativeRaf(ts => { const item = pending.get(id); if (!item) return;" +
            "if (ts - lastFrame >= interval) { lastFrame = ts; pending.delete(id); item.fn(ts); } else wait(id); });" +
            "window.requestAnimationFrame = fn => { const id = nextId++; pending.set(id, { fn }); wait(id); return id; };" +
            "window.cancelAnimationFrame = id => { pending.delete(id); nativeCancel(id); };" +
            "runtime.__frameLimiterInstalled = true; window.deskCanvas = runtime; })();");
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
        core.ClearVirtualHostNameToFolderMapping("wallpaper.deskcanvas");
        core.SetVirtualHostNameToFolderMapping("wallpaper.deskcanvas", Path.GetFullPath(_folder), CoreWebView2HostResourceAccessKind.Allow);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => completion.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            string relativeEntry = Path.GetRelativePath(_folder, entry).Replace('\\', '/');
            string virtualEntry = string.Join('/', relativeEntry.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
            core.Navigate($"https://wallpaper.deskcanvas/{virtualEntry}");
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
        string properties = JsonSerializer.Serialize(_manifest.Properties, RuntimeJson);
        string settings = JsonSerializer.Serialize(_propertyValues, RuntimeJson);
        string metadata = JsonSerializer.Serialize(new
        {
            _manifest.Id,
            _manifest.Title,
            _manifest.Author,
            _manifest.Version,
            _manifest.Type,
            _manifest.AudioReactive,
            _manifest.RecommendedFps,
            Monitor = new
            {
                DeviceName = ScreenDeviceName,
                Bounds = new { ScreenBounds.X, ScreenBounds.Y, ScreenBounds.Width, ScreenBounds.Height },
                Dpi = DeviceDpi
            }
        }, RuntimeJson);
        string script = "(() => {" +
            "const runtime = window.deskCanvas || (window.deskCanvas = {});" +
            "runtime.metadata = " + metadata + ";" +
            "runtime.properties = " + properties + ";" +
            "runtime.settings = " + settings + ";" +
            "runtime.display = runtime.metadata.monitor;" +
            "runtime.recommendedFps = runtime.metadata.recommendedFps;" +
            "runtime.maxFps = runtime.recommendedFps;" +
            "runtime.audio = runtime.audio || { rms: 0, peak: 0, spectrum: [] };" +
            "if (typeof runtime.setFrameRate === 'function') runtime.setFrameRate(runtime.recommendedFps);" +
            "window.dispatchEvent(new CustomEvent('deskcanvas:ready', { detail: runtime }));" +
            "})();";
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write("壁纸运行时初始化失败", ex); }
    }

    public async Task SetPropertyAsync(string key, JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(key) || _web.CoreWebView2 is null) return;
        var values = new Dictionary<string, JsonElement>(_propertyValues, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };
        _propertyValues = new ReadOnlyDictionary<string, JsonElement>(values);
        string keyJson = JsonSerializer.Serialize(key);
        string valueJson = JsonSerializer.Serialize(value);
        string script = "(() => { const runtime = window.deskCanvas || (window.deskCanvas = {}); " +
            "runtime.settings = runtime.settings || {}; runtime.settings[" + keyJson + "] = " + valueJson + "; " +
            "window.dispatchEvent(new CustomEvent('deskcanvas:property-changed', { detail: { key: " + keyJson + ", value: " + valueJson + " } })); })();";
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write("Wallpaper property sync failed", ex); }
    }

    private async Task HandleWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
            JsonElement message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("type", out JsonElement typeElement)) return;
            string? type = typeElement.GetString();
            switch (type?.ToLowerInvariant())
            {
                case "ready":
                case "hello":
                case "requestruntime":
                    await InitializeRuntimeAsync();
                    break;
                case "pause":
                    SetPaused(true);
                    break;
                case "resume":
                    SetPaused(false);
                    break;
                case "setframerate":
                    if (message.TryGetProperty("fps", out JsonElement fps) && fps.TryGetInt32(out int requestedFps))
                    {
                        int clamped = Math.Clamp(requestedFps, 1, 240);
                        await ExecuteRuntimeScriptAsync($"window.deskCanvas?.setFrameRate?.({clamped});");
                    }
                    break;
                case "setproperty":
                    if (message.TryGetProperty("key", out JsonElement keyElement) && message.TryGetProperty("value", out JsonElement valueElement))
                    {
                        string? key = keyElement.GetString();
                        if (!string.IsNullOrWhiteSpace(key)) await SetPropertyAsync(key, valueElement.Clone());
                    }
                    break;
                case "log":
                    if (message.TryGetProperty("message", out JsonElement logElement)) StartupLog.Write($"Wallpaper: {logElement}");
                    break;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write("Wallpaper web message handling failed", ex);
        }
    }

    private async Task ExecuteRuntimeScriptAsync(string script)
    {
        if (_web.CoreWebView2 is null) return;
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write("Wallpaper runtime command failed", ex); }
    }

    public void PushAudioFrame(AudioFrame frame)
    {
        if (!AudioReactiveEnabled || _paused) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => PushAudioFrame(frame)); }
            catch (InvalidOperationException) { }
            return;
        }
        if (IsDisposed || _closing || _web.CoreWebView2 is null) return;
        string payload = JsonSerializer.Serialize(frame, RuntimeJson);
        _ = ExecuteRuntimeScriptAsync("(() => { const runtime = window.deskCanvas || (window.deskCanvas = {}); " +
            "runtime.audio = " + payload + "; window.dispatchEvent(new CustomEvent('deskcanvas:audio', { detail: runtime.audio })); })();");
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
                        // Record media that was playing before the host pauses it. On resume,
                        // only those elements are restarted; user-paused media stays paused.
                        "document.querySelectorAll('video, audio').forEach(media => {" +
                        "if (paused) {" +
                        "if (!media.paused && !media.ended) media.dataset.deskCanvasWasPlaying = 'true';" +
                        "media.pause();" +
                        "} else if (media.dataset.deskCanvasWasPlaying === 'true') {" +
                        "media.play().catch(() => {});" +
                        "delete media.dataset.deskCanvasWasPlaying;" +
                        "}" +
                        "});" +
                        "window.dispatchEvent(new CustomEvent('deskcanvas:" + state + "', { detail: { source: 'host' } }));" +
                        "})();";
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { StartupLog.Write($"壁纸{state}协议执行失败", ex); }
    }

    public void ForwardMouse(int x, int y, uint message, uint mouseData = 0)
    {
        if (_paused || _manifest?.Interactive != true || _web.CoreWebView2 is null) return;
        if (!ScreenBounds.Contains(x, y)) return;
        int localX = x - ScreenBounds.Left;
        int localY = y - ScreenBounds.Top;
        int localWidth = Math.Max(1, ScreenBounds.Width);
        int localHeight = Math.Max(1, ScreenBounds.Height);
        double xRatio = (double)localX / localWidth;
        double yRatio = (double)localY / localHeight;
        string type = message switch
        {
            NativeMethods.WmMouseMove => "mousemove",
            NativeMethods.WmLButtonDown or NativeMethods.WmMButtonDown or NativeMethods.WmRButtonDown => "mousedown",
            NativeMethods.WmLButtonUp or NativeMethods.WmMButtonUp or NativeMethods.WmRButtonUp => "mouseup",
            NativeMethods.WmMouseWheel => "wheel",
            _ => ""
        };
        if (type.Length == 0) return;
        int button = message is NativeMethods.WmRButtonDown or NativeMethods.WmRButtonUp ? 2 : message is NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp ? 1 : 0;
        int deltaY = message == NativeMethods.WmMouseWheel ? (short)((mouseData >> 16) & 0xffff) : 0;
        string eventConstructor = message == NativeMethods.WmMouseWheel ? "WheelEvent" : "MouseEvent";
        string options = $"{{clientX:window.innerWidth*{xRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)},clientY:window.innerHeight*{yRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)},bubbles:true,button:{button},deltaY:{deltaY}}}";
        string script = $"window.dispatchEvent(new {eventConstructor}({JsonSerializer.Serialize(type)},{options}))";
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
