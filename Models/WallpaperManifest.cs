using System.Text.Json;

namespace DeskCanvas.Models;

/// <summary>壁纸包根目录中的 manifest.json 描述。</summary>
public sealed class WallpaperManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名壁纸";
    public string Type { get; set; } = "web";
    public string Category { get; set; } = "特效壁纸";
    public List<string> Tags { get; set; } = new();
    public string Entry { get; set; } = "index.html";
    public bool Interactive { get; set; } = true;

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
        return value;
    }
}
