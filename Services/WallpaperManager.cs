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
    private readonly SettingsStore _settings = new();
    private readonly AudioReactiveService _audio = new();
    private readonly SemaphoreSlim _playlistGate = new(1, 1);
    public bool AutoPause
    {
        get => _settings.Current.AutoPause;
        set
        {
            if (_settings.Current.AutoPause == value) return;
            _settings.Current.AutoPause = value;
            _settings.Save();
        }
    }
    public bool WallpaperAboveIcons
    {
        get => _settings.Current.WallpaperAboveIcons;
        set
        {
            if (_settings.Current.WallpaperAboveIcons == value) return;
            _settings.Current.WallpaperAboveIcons = value;
            _settings.Save();
        }
    }
    public bool StartupEnabled
    {
        get => _settings.Current.StartupEnabled;
        set
        {
            if (_settings.Current.StartupEnabled == value) return;
            _settings.Current.StartupEnabled = value;
            _settings.Save();
            StartupManager.Configure(value);
        }
    }
    public bool IsPaused { get; private set; }
    public IReadOnlyList<Screen> Screens => Screen.AllScreens;
    public string? CurrentFolder { get; private set; }
    public IReadOnlyList<string> RunningWallpaperFolders => _windows.Values
        .Select(window => window.WallpaperFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<(string Folder, WallpaperManifest Manifest)> Library => DiscoverLibrary();

    /// <summary>Whether the global wallpaper should advance through the configured playlist.</summary>
    public bool PlaylistEnabled
    {
        get => _settings.Current.PlaylistEnabled;
        set
        {
            if (_settings.Current.PlaylistEnabled == value) return;
            _settings.Current.PlaylistEnabled = value;
            _settings.Current.PlaylistNextRotationUtc = value
                ? DateTime.UtcNow.AddMinutes(PlaylistIntervalMinutes)
                : null;
            _settings.Save();
        }
    }

    /// <summary>Minutes between automatic playlist changes. Values are clamped to one day.</summary>
    public int PlaylistIntervalMinutes
    {
        get => Math.Clamp(_settings.Current.PlaylistIntervalMinutes, 1, 1440);
        set
        {
            int interval = Math.Clamp(value, 1, 1440);
            if (_settings.Current.PlaylistIntervalMinutes == interval) return;
            _settings.Current.PlaylistIntervalMinutes = interval;
            if (PlaylistEnabled) _settings.Current.PlaylistNextRotationUtc = DateTime.UtcNow.AddMinutes(interval);
            _settings.Save();
        }
    }

    /// <summary>Folders included in automatic rotation. An empty list means the discovered library.</summary>
    public IReadOnlyList<string> PlaylistFolders => _settings.Current.PlaylistFolders.ToList();

    public WallpaperManager()
    {
        // System.Text.Json does not preserve the comparer used by dictionaries when
        // deserializing. Normalize the persisted maps so device names and property
        // keys continue to be matched case-insensitively.
        _settings.Current.WallpaperAssignments = new Dictionary<string, string>(
            _settings.Current.WallpaperAssignments ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        _settings.Current.WallpaperProperties = (_settings.Current.WallpaperProperties ?? new Dictionary<string, Dictionary<string, JsonElement>>())
            .ToDictionary(item => item.Key,
                item => new Dictionary<string, JsonElement>(item.Value ?? new Dictionary<string, JsonElement>(), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        _settings.Current.PlaylistFolders ??= new List<string>();
        _settings.Current.PlaylistIntervalMinutes = Math.Clamp(_settings.Current.PlaylistIntervalMinutes, 1, 1440);
        if (_settings.Current.PlaylistEnabled && !_settings.Current.PlaylistNextRotationUtc.HasValue)
        {
            _settings.Current.PlaylistNextRotationUtc = DateTime.UtcNow.AddMinutes(_settings.Current.PlaylistIntervalMinutes);
            _settings.Save();
        }
        CurrentFolder = _settings.Current.LastWallpaperFolder is { } folder && Directory.Exists(folder) ? folder : null;
        _audio.FrameAvailable += OnAudioFrameAvailable;
    }

    public void LoadSelection(string folder)
    {
        _ = WallpaperManifest.Load(folder);
        string fullFolder = Path.GetFullPath(folder);
        CurrentFolder = fullFolder;
        _settings.Current.LastWallpaperFolder = fullFolder;
        int playlistIndex = _settings.Current.PlaylistFolders.FindIndex(folder => string.Equals(folder, fullFolder, StringComparison.OrdinalIgnoreCase));
        if (playlistIndex >= 0) _settings.Current.PlaylistIndex = playlistIndex;
        if (PlaylistEnabled) _settings.Current.PlaylistNextRotationUtc = DateTime.UtcNow.AddMinutes(PlaylistIntervalMinutes);
        _settings.Save();
    }

    /// <summary>Replaces the persisted playlist. Empty input uses every valid item in the library.</summary>
    public void SetPlaylist(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var normalized = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(folder => Directory.Exists(folder))
            .Where(folder => TryLoad(folder) is not null)
            .ToList();
        _settings.Current.PlaylistFolders = normalized;
        _settings.Current.PlaylistIndex = CurrentFolder is null
            ? -1
            : normalized.FindIndex(folder => string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase));
        _settings.Current.PlaylistNextRotationUtc = PlaylistEnabled
            ? DateTime.UtcNow.AddMinutes(PlaylistIntervalMinutes)
            : null;
        _settings.Save();
    }

    /// <summary>Advances the global wallpaper when the persisted rotation deadline has elapsed.</summary>
    public async Task<bool> RotatePlaylistAsync(bool force = false)
    {
        if (!PlaylistEnabled) return false;
        await _playlistGate.WaitAsync();
        try
        {
            DateTime now = DateTime.UtcNow;
            DateTime? nextRotation = _settings.Current.PlaylistNextRotationUtc;
            if (!force && nextRotation.HasValue && now < nextRotation.Value) return false;

            List<string> playlist = GetEffectivePlaylist();
            if (playlist.Count == 0) return false;

            int currentIndex = _settings.Current.PlaylistIndex;
            if (currentIndex < 0 || currentIndex >= playlist.Count ||
                (CurrentFolder is not null && !string.Equals(playlist[currentIndex], CurrentFolder, StringComparison.OrdinalIgnoreCase)))
            {
                currentIndex = CurrentFolder is null
                    ? -1
                    : playlist.FindIndex(folder => string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase));
            }
            int nextIndex = playlist.Count == 1 ? 0 : (currentIndex + 1 + playlist.Count) % playlist.Count;
            string nextFolder = playlist[nextIndex];
            _settings.Current.PlaylistIndex = nextIndex;
            _settings.Current.PlaylistNextRotationUtc = now.AddMinutes(PlaylistIntervalMinutes);
            CurrentFolder = nextFolder;
            _settings.Current.LastWallpaperFolder = nextFolder;
            _settings.Save();
            await ApplyToAll();
            return true;
        }
        finally
        {
            _playlistGate.Release();
        }
    }

    private List<string> GetEffectivePlaylist()
    {
        var configured = _settings.Current.PlaylistFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(folder => Directory.Exists(folder))
            .Where(folder => TryLoad(folder) is not null)
            .ToList();
        if (configured.Count > 0) return configured;
        return DiscoverLibrary().Select(item => item.Folder).ToList();
    }

    /// <summary>Returns the explicit assignment for a display, without applying the default.</summary>
    public string? GetAssignedWallpaperFolder(Screen screen) => GetAssignedWallpaperFolder(screen.DeviceName);

    /// <summary>Returns the explicit assignment for a display, without applying the default.</summary>
    public string? GetAssignedWallpaperFolder(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;
        return _settings.Current.WallpaperAssignments.TryGetValue(deviceName, out string? folder)
            ? folder
            : null;
    }

    /// <summary>Returns the folder that will be used for a display, falling back to the global selection.</summary>
    public string? GetWallpaperFolder(Screen screen) => ResolveWallpaperFolder(screen);

    /// <summary>Returns a snapshot of explicit per-display assignments.</summary>
    public IReadOnlyDictionary<string, string> WallpaperAssignments =>
        new Dictionary<string, string>(_settings.Current.WallpaperAssignments, StringComparer.OrdinalIgnoreCase);

    /// <summary>Persists a display-specific wallpaper folder. Null clears the assignment.</summary>
    public void SetWallpaperFolder(Screen screen, string? folder) => SetWallpaperFolder(screen.DeviceName, folder);

    /// <summary>Persists a display-specific wallpaper folder. Null clears the assignment.</summary>
    public void SetWallpaperFolder(string deviceName, string? folder)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("显示器名称不能为空", nameof(deviceName));
        if (string.IsNullOrWhiteSpace(folder))
        {
            _settings.Current.WallpaperAssignments.Remove(deviceName);
        }
        else
        {
            string fullFolder = Path.GetFullPath(folder);
            _ = WallpaperManifest.Load(fullFolder);
            _settings.Current.WallpaperAssignments[deviceName] = fullFolder;
        }
        _settings.Save();
    }

    public void ClearWallpaperFolder(Screen screen) => SetWallpaperFolder(screen.DeviceName, null);

    /// <summary>Clears all display-specific overrides so the global wallpaper is used everywhere.</summary>
    public void ClearWallpaperAssignments()
    {
        if (_settings.Current.WallpaperAssignments.Count == 0) return;
        _settings.Current.WallpaperAssignments.Clear();
        _settings.Save();
    }

    private string? ResolveWallpaperFolder(Screen screen)
    {
        string? assigned = GetAssignedWallpaperFolder(screen);
        // A display assignment can outlive an imported/deleted package or point at
        // a directory whose manifest is broken. Treat those as unavailable and use
        // the global wallpaper instead of silently leaving the display uncovered.
        if (TryGetValidWallpaperFolder(assigned) is { } assignedFolder) return assignedFolder;
        return TryGetValidWallpaperFolder(CurrentFolder);
    }

    private static string? TryGetValidWallpaperFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            string fullFolder = Path.GetFullPath(folder);
            return Directory.Exists(fullFolder) && TryLoad(fullFolder) is not null ? fullFolder : null;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyDictionary<string, JsonElement> GetPropertyValues(WallpaperManifest manifest)
    {
        return _settings.Current.WallpaperProperties.TryGetValue(manifest.Id, out var values)
            ? values
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetPropertyAsync(string key, JsonElement value)
    {
        if (CurrentFolder is null) return;
        WallpaperManifest manifest = WallpaperManifest.Load(CurrentFolder);
        if (!manifest.Properties.TryGetValue(key, out WallpaperProperty? property)) throw new InvalidOperationException($"壁纸不支持属性：{key}");
        ValidatePropertyValue(key, property, value);
        if (!_settings.Current.WallpaperProperties.TryGetValue(manifest.Id, out var values))
        {
            values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            _settings.Current.WallpaperProperties[manifest.Id] = values;
        }
        values[key] = value.Clone();
        _settings.Save();
        foreach (WallpaperWindow window in _windows.Values)
        {
            if (window.SupportsProperty(key)) await window.SetPropertyAsync(key, value);
        }
    }
    private static void ValidatePropertyValue(string key, WallpaperProperty property, JsonElement value)
    {
        string type = property.Type.Trim().ToLowerInvariant();
        if (type == "number")
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number)) throw new InvalidDataException($"属性 {key} 必须是数字");
            if (property.Min is { } min && number < min || property.Max is { } max && number > max) throw new InvalidDataException($"属性 {key} 超出允许范围");
        }
        else if (type is "boolean" or "bool")
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"属性 {key} 必须是布尔值");
        }
        else if (type is "select" or "option")
        {
            if (value.ValueKind != JsonValueKind.String || !property.Options.Contains(value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException($"属性 {key} 的选项无效");
        }
    }

    public void ImportFolder(string folder, WallpaperManifest? manifest = null)
    {
        string source = Path.GetFullPath(folder);
        bool hasManifest = File.Exists(Path.Combine(source, "manifest.json"));
        if (hasManifest)
        {
            _ = WallpaperManifest.Load(source);
        }
        else if (manifest is null)
        {
            throw new FileNotFoundException("目录中缺少 manifest.json", Path.Combine(source, "manifest.json"));
        }
        string libraryRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeskCanvas", "Imported");
        Directory.CreateDirectory(libraryRoot);
        string name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string target = Path.Combine(libraryRoot, name);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            LoadSelection(source);
            return;
        }
        if (Directory.Exists(target)) target = Path.Combine(libraryRoot, $"{name}-{Guid.NewGuid():N}");
        CopyDirectory(source, target);
        if (!hasManifest)
        {
            File.WriteAllText(Path.Combine(target, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        LoadSelection(target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

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

    public bool CanDelete(string folder)
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeskCanvas"));
            string candidate = Path.GetFullPath(folder);
            return Directory.Exists(candidate) && IsPathInside(root, candidate);
        }
        catch
        {
            return false;
        }
    }

    public void DeleteWallpaper(string folder)
    {
        if (!CanDelete(folder)) throw new InvalidOperationException("内置壁纸不可删除，只能删除用户导入或创建的壁纸。");
        string fullFolder = Path.GetFullPath(folder);
        WallpaperManifest manifest = WallpaperManifest.Load(fullFolder);

        foreach (string device in _windows
                     .Where(item => string.Equals(item.Value.WallpaperFolder, fullFolder, StringComparison.OrdinalIgnoreCase))
                     .Select(item => item.Key).ToList())
        {
            _ = _windows[device].CloseAsync();
            _windows.Remove(device);
        }
        UsesCoverWindow = _windows.Values.Any(window => window.UsesCoverWindow);
        UpdateAudioCapture();
        if (string.Equals(CurrentFolder, fullFolder, StringComparison.OrdinalIgnoreCase)) CurrentFolder = null;
        if (string.Equals(_settings.Current.LastWallpaperFolder, fullFolder, StringComparison.OrdinalIgnoreCase)) _settings.Current.LastWallpaperFolder = null;
        foreach (string device in _settings.Current.WallpaperAssignments
                     .Where(item => string.Equals(item.Value, fullFolder, StringComparison.OrdinalIgnoreCase))
                     .Select(item => item.Key).ToList())
            _settings.Current.WallpaperAssignments.Remove(device);
        _settings.Current.PlaylistFolders.RemoveAll(item => string.Equals(item, fullFolder, StringComparison.OrdinalIgnoreCase));
        _settings.Current.PlaylistIndex = -1;
        _settings.Current.WallpaperProperties.Remove(manifest.Id);
        _settings.Save();

        Directory.Delete(fullFolder, true);
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

    /// <summary>
    /// Resolves an image that can be used as a library thumbnail. A package may
    /// provide an explicit manifest preview, or simply contain an image asset.
    /// Invalid/missing assets are ignored so one broken package cannot prevent
    /// the library from loading.
    /// </summary>
    public string? GetPreviewPath(string folder, WallpaperManifest manifest)
    {
        try
        {
            string root = Path.GetFullPath(folder);
            if (!Directory.Exists(root)) return null;

            if (!string.IsNullOrWhiteSpace(manifest.Preview))
            {
                string explicitPreview = Path.GetFullPath(Path.Combine(root, manifest.Preview));
                if (IsPathInside(root, explicitPreview) && File.Exists(explicitPreview) && IsImageAsset(explicitPreview))
                    return explicitPreview;
            }

            string entry = Path.GetFullPath(Path.Combine(root, manifest.Entry));
            if (IsPathInside(root, entry) && File.Exists(entry) && IsImageAsset(entry)) return entry;

            // Imported media and simple packages commonly keep their image at
            // the package root without declaring a Preview field.
            return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(IsImageAsset)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImageAsset(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp";

    private static bool IsPathInside(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
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

    public async Task ApplyTo(IEnumerable<Screen> screens, bool replaceAll = false)
    {
        var selectedScreens = screens.DistinctBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedScreens.Count == 0) return;
        var pending = replaceAll
            ? new Dictionary<string, WallpaperWindow>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WallpaperWindow>(_windows, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        // 逐个显示器尽力应用：某个显示器失败（例如投影/虚拟显示器）不应影响其它显示器。
        foreach (Screen screen in selectedScreens)
        {
            string? folder = ResolveWallpaperFolder(screen);
            if (folder is null) continue;

            WallpaperManifest manifest;
            try
            {
                manifest = WallpaperManifest.Load(folder);
            }
            catch (Exception ex)
            {
                failures.Add($"{screen.DeviceName} ({screen.Bounds.Width} × {screen.Bounds.Height}): {ex.Message}");
                StartupLog.Write($"显示器 {screen.Bounds} 的壁纸清单加载失败", ex);
                continue;
            }

            if (_windows.TryGetValue(screen.DeviceName, out WallpaperWindow? reusable) && reusable.IsMounted &&
                string.Equals(reusable.WallpaperFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                reusable.SetDesktopLayer(WallpaperAboveIcons);
                pending[screen.DeviceName] = reusable;
                continue;
            }
            try
            {
                // 不复用不同壁纸的 WebView2 页面，避免旧脚本、定时器和 DOM 状态污染新壁纸。
                WallpaperWindow replacement = await CreateWindowAsync(screen, folder, manifest);
                pending[screen.DeviceName] = replacement;
                if (_windows.TryGetValue(screen.DeviceName, out WallpaperWindow? previous) && !ReferenceEquals(previous, replacement))
                    await previous.CloseAsync();
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
        // 全屏应用时关闭已移除显示器或不再使用的旧窗口；单屏应用保留其它显示器。
        foreach (var item in _windows)
        {
            if (replaceAll && !pending.ContainsKey(item.Key)) await item.Value.CloseAsync();
        }
        _windows.Clear();
        foreach (var item in pending) _windows[item.Key] = item.Value;
        LastFailures = failures;
        UsesCoverWindow = pending.Values.Any(item => item.UsesCoverWindow);
        UpdateAudioCapture();
    }
    /// <summary>为显示器新建壁纸窗口；第一次失败时丢弃缓存的 WebView2 环境并用新窗口重试一次。</summary>
    private async Task<WallpaperWindow> CreateWindowAsync(Screen screen, string folder, WallpaperManifest manifest)
    {
        var window = new WallpaperWindow(screen);
        try
        {
            await window.StartAsync(folder, manifest, GetPropertyValues(manifest));
            window.SetDesktopLayer(WallpaperAboveIcons);
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
        await retry.StartAsync(folder, manifest, GetPropertyValues(manifest));
        retry.SetDesktopLayer(WallpaperAboveIcons);
        return retry;
    }
    public Task ApplyToAll() => ApplyTo(Screens, replaceAll: true);
    public void Pause(bool paused) { IsPaused = paused; foreach (var window in _windows.Values) window.SetPaused(paused); }
    /// <summary>覆盖模式下系统"显示桌面"可能把壁纸窗口最小化，这里做一次兜底恢复。</summary>
    public void EnsureVisible() { foreach (var window in _windows.Values) window.EnsureVisible(); }
    public void CloseWallpapers()
    {
        // CloseAsync 内部没有真正的异步等待，调用后即已完成。
        foreach (var window in _windows.Values) _ = window.CloseAsync();
        _windows.Clear();
        UpdateAudioCapture();
    }

    public void MarkManualExit()
    {
        _settings.Current.StartupEnabled = false;
        _settings.Save();
        StartupManager.Disable();
    }

    private void UpdateAudioCapture()
    {
        if (_windows.Values.Any(window => window.AudioReactiveEnabled)) _audio.TryStart();
        else _audio.Stop();
    }

    private void OnAudioFrameAvailable(object? sender, AudioFrame frame)
    {
        foreach (WallpaperWindow window in _windows.Values) window.PushAudioFrame(frame);
    }
    public static void ForwardMouse(int x, int y, uint message, uint mouseData = 0)
    {
        foreach (var window in WallpaperWindow.Instances)
            if (window.IsMounted && window.ScreenBounds.Contains(x, y)) { window.ForwardMouse(x, y, message, mouseData); break; }
    }
    public void Dispose()
    {
        CloseWallpapers();
        _audio.FrameAvailable -= OnAudioFrameAvailable;
        _audio.Dispose();
        _mouse.Dispose();
        _playlistGate.Dispose();
    }
}
