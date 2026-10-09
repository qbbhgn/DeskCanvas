using DeskCanvas.Interop;
using DeskCanvas.Models;
using DeskCanvas.UI;
using System.Text.Json;

namespace DeskCanvas.Services;

/// <summary>管理每个显示器的壁纸窗体、素材选择与自动暂停状态。</summary>
internal sealed class WallpaperManager : IDisposable
{
    private readonly Dictionary<string, WallpaperWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly MouseForwarder _mouse = new();
    public bool AutoPause { get; set; } = true;
    public bool IsPaused { get; private set; }
    public IReadOnlyList<Screen> Screens => Screen.AllScreens;
    public string? CurrentFolder { get; private set; }
    public IReadOnlyList<(string Folder, WallpaperManifest Manifest)> Library => DiscoverLibrary();
    public void LoadSelection(string folder)
    {
        _ = WallpaperManifest.Load(folder);
        CurrentFolder = folder;
    }
    public void ImportFolder(string folder) => LoadSelection(folder);
    public string ImportFile(string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        string type = extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" => "image",
            ".mp4" or ".webm" or ".mov" or ".avi" => "video",
            _ => throw new InvalidDataException("仅支持图片或视频文件。")
        };
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeskCanvas", "Imported", Path.GetFileNameWithoutExtension(file));
        Directory.CreateDirectory(root);
        string asset = Path.Combine(root, Path.GetFileName(file));
        File.Copy(file, asset, true);
        string entry = "index.html";
        string source = type == "video"
            ? $"<!doctype html><html><body><video src=\"{Uri.EscapeDataString(Path.GetFileName(file))}\" autoplay muted loop playsinline></video><style>html,body,video{{margin:0;width:100%;height:100%;overflow:hidden;background:#000}}video{{object-fit:cover}}</style></body></html>"
            : $"<!doctype html><html><body><img src=\"{Uri.EscapeDataString(Path.GetFileName(file))}\"><style>html,body,img{{margin:0;width:100%;height:100%;overflow:hidden;background:#000}}img{{object-fit:cover}}</style></body></html>";
        File.WriteAllText(Path.Combine(root, entry), source);
        var manifest = new WallpaperManifest { Id = Guid.NewGuid().ToString("N"), Title = Path.GetFileNameWithoutExtension(file), Type = type, Category = type == "video" ? "视频壁纸" : "静态壁纸", Interactive = false, Entry = entry, Tags = new List<string> { type == "video" ? "视频" : "图片", "导入" } };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return root;
    }
    public IReadOnlyList<(string Folder, WallpaperManifest Manifest)> DiscoverLibrary()
    {
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Wallpapers"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeskCanvas")
        };
        return roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            .Select(folder => (Folder: folder, Manifest: TryLoad(folder)))
            .Where(item => item.Manifest is not null)
            .Select(item => (item.Folder, item.Manifest!))
            .DistinctBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static WallpaperManifest? TryLoad(string folder)
    {
        try { return WallpaperManifest.Load(folder); }
        catch { return null; }
    }
    /// <summary>上一次应用时创建失败的显示器描述（仅记录，不影响其它显示器）。</summary>
    public IReadOnlyList<string> LastFailures { get; private set; } = Array.Empty<string>();
    /// <summary>true 表示系统没有可挂接的桌面背景层，壁纸改以覆盖桌面的普通窗口运行（会遮住桌面图标）。</summary>
    public bool UsesCoverWindow { get; private set; }
    public async Task ApplyTo(IEnumerable<Screen> screens)
    {
        if (CurrentFolder is null || !Directory.Exists(CurrentFolder)) return;
        WallpaperManifest manifest = WallpaperManifest.Load(CurrentFolder);
        var selectedScreens = screens.DistinctBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
        var pending = new Dictionary<string, WallpaperWindow>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        // 逐个显示器尽力应用：某个显示器失败（例如投影/虚拟显示器）不应影响其它显示器。
        foreach (Screen screen in selectedScreens)
        {
            if (_windows.TryGetValue(screen.DeviceName, out WallpaperWindow? reusable) && reusable.IsMounted)
            {
                // 复用已挂接的窗口，只切换网页内容：不重建 WebView2，既不闪屏也不会重复初始化失败。
                await reusable.StartAsync(CurrentFolder, manifest);
                pending[screen.DeviceName] = reusable;
                continue;
            }
            try
            {
                pending[screen.DeviceName] = await CreateWindowAsync(screen, manifest);
            }
            catch (Exception ex)
            {
                failures.Add($"{screen.DeviceName}（{screen.Bounds.Width} × {screen.Bounds.Height}）: {ex.Message}");
                StartupLog.Write($"显示器 {screen.Bounds} 的壁纸创建失败", ex);
            }
        }
        if (pending.Count == 0 && failures.Count > 0)
        {
            LastFailures = failures;
            throw new InvalidOperationException("所有显示器的壁纸都创建失败，请查看日志：" + StartupLog.GetPath());
        }
        // 关闭本次不再使用的旧窗口（显示器被移除或取消勾选）。
        foreach (var item in _windows)
        {
            if (!pending.ContainsKey(item.Key)) await item.Value.CloseAsync();
        }
        _windows.Clear();
        foreach (var item in pending) _windows[item.Key] = item.Value;
        LastFailures = failures;
        UsesCoverWindow = pending.Values.Any(item => item.UsesCoverWindow);
    }
    /// <summary>为显示器新建壁纸窗口；第一次失败时丢弃缓存的 WebView2 环境并用新窗口重试一次。</summary>
    private async Task<WallpaperWindow> CreateWindowAsync(Screen screen, WallpaperManifest manifest)
    {
        var window = new WallpaperWindow(screen);
        try
        {
            await window.StartAsync(CurrentFolder!, manifest);
            return window;
        }
        catch (Exception ex)
        {
            await window.CloseAsync();
            // WebView2 运行时在应用运行期间被更新、或浏览器进程已退出时，缓存的 Environment 会失效，
            // 官方建议此时重新创建 Environment（CreateCoreWebView2ControllerAsync 的备注）。
            WallpaperWindow.ResetEnvironment();
            StartupLog.Write($"显示器 {screen.Bounds} 的壁纸初始化失败，重建 WebView2 环境后重试", ex);
        }
        var retry = new WallpaperWindow(screen);
        await retry.StartAsync(CurrentFolder!, manifest);
        return retry;
    }
    public Task ApplyToAll() => ApplyTo(Screens);
    public void Pause(bool paused) { IsPaused = paused; foreach (var window in _windows.Values) window.SetPaused(paused); }
    /// <summary>覆盖模式下系统"显示桌面"可能把壁纸窗口最小化，这里做一次兜底恢复。</summary>
    public void EnsureVisible() { foreach (var window in _windows.Values) window.EnsureVisible(); }
    public void CloseWallpapers()
    {
        // CloseAsync 内部没有真正的异步等待，调用后即已完成。
        foreach (var window in _windows.Values) _ = window.CloseAsync();
        _windows.Clear();
    }
    public static void ForwardMouse(int x, int y, uint message)
    {
        foreach (var window in WallpaperWindow.Instances)
            if (window.IsMounted && window.ScreenBounds.Contains(x, y)) { window.ForwardMouse(x, y, message); break; }
    }
    public void Dispose() { CloseWallpapers(); _mouse.Dispose(); }
}
