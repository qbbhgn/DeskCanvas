using System.Text.Json;

namespace DeskCanvas.Services;

/// <summary>Persists the small amount of user state needed by the wallpaper runtime.</summary>
internal sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DeskCanvas",
        "settings.json");

    public DeskCanvasSettings Current { get; private set; }

    public SettingsStore()
    {
        Current = Load();
    }

    public void Save()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private DeskCanvasSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<DeskCanvasSettings>(File.ReadAllText(_path), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new DeskCanvasSettings();
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write("读取 DeskCanvas 设置失败，将使用默认设置", ex);
        }
        return new DeskCanvasSettings();
    }
}

internal sealed class DeskCanvasSettings
{
    public bool StartupEnabled { get; set; } = true;
    public bool AutoPause { get; set; } = true;
    public bool WallpaperAboveIcons { get; set; }
    public string? LastWallpaperFolder { get; set; }
    public Dictionary<string, string> WallpaperAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, JsonElement>> WallpaperProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool PlaylistEnabled { get; set; }
    public int PlaylistIntervalMinutes { get; set; } = 30;
    public List<string> PlaylistFolders { get; set; } = new();
    public int PlaylistIndex { get; set; } = -1;
    public DateTime? PlaylistNextRotationUtc { get; set; }
}
