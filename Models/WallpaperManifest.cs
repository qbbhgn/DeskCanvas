using System.Text.Json;

namespace DeskCanvas.Models;

/// <summary>壁纸包根目录中的 manifest.json 描述。</summary>
public sealed class WallpaperManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名壁纸";
    public string Author { get; set; } = "未知作者";
    public string Version { get; set; } = "1.0.0";
    public string Type { get; set; } = "web";
    public string Category { get; set; } = "特效壁纸";
    public List<string> Tags { get; set; } = new();
    public string Entry { get; set; } = "index.html";
    public string? Preview { get; set; }
    public bool Interactive { get; set; } = true;
    public bool AudioReactive { get; set; }
    public int RecommendedFps { get; set; } = 60;
    public Dictionary<string, WallpaperProperty> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static WallpaperManifest Load(string folder)
    {
        string path = Path.Combine(folder, "manifest.json");
        if (!File.Exists(path)) throw new FileNotFoundException("目录中缺少 manifest.json", path);
        var value = JsonSerializer.Deserialize<WallpaperManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("manifest.json 内容无效");
        if (string.IsNullOrWhiteSpace(value.Entry)) throw new InvalidDataException("manifest.json 的 Entry 不能为空");
        string full = Path.GetFullPath(Path.Combine(folder, value.Entry));
        if (!full.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entry 不能引用壁纸目录之外的文件");
        if (!File.Exists(full)) throw new FileNotFoundException("找不到壁纸入口文件", full);
        if (value.RecommendedFps is < 1 or > 240) throw new InvalidDataException("RecommendedFps 必须在 1 到 240 之间");
        if (!string.IsNullOrWhiteSpace(value.Preview))
        {
            string preview = Path.GetFullPath(Path.Combine(folder, value.Preview));
            if (!preview.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Preview 不能引用壁纸目录之外的文件");
            if (!File.Exists(preview)) throw new FileNotFoundException("找不到壁纸预览图", preview);
        }
        return value;
    }
}

public sealed class WallpaperProperty
{
    public string Type { get; set; } = "text";
    public JsonElement Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public List<string> Options { get; set; } = new();
}
