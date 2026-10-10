using System.Text.Json;

namespace DeskCanvas.Models;

/// <summary>壁纸包根目录中的 manifest.json 描述。</summary>
public sealed class WallpaperManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名壁纸";
    public string Author { get; set; } = "未知作者";
    public string Description { get; set; } = "";
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

    /// <summary>返回清单自定义分类以及可由宿主能力判断的分类。</summary>
    public IReadOnlyList<string> GetCategories(string? folder = null)
    {
        var categories = new List<string>();
        if (!IsDerivedCategory(Category)) AddCategory(categories, Category);

        if (string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase))
        {
            bool animated = !string.IsNullOrWhiteSpace(folder) && ContainsAnimatedImage(folder);
            AddCategory(categories, animated ? "动态图片" : "静态图片");
            AddCategory(categories, animated ? "动态壁纸" : "静态壁纸");
            AddCategory(categories, "图片壁纸");
        }
        else if (string.Equals(Type, "video", StringComparison.OrdinalIgnoreCase)) AddCategory(categories, "动态壁纸");
        else AddCategory(categories, "动态壁纸");

        if (Interactive) AddCategory(categories, "可交互壁纸");
        if (AudioReactive) AddCategory(categories, "音频响应");
        if (string.Equals(Type, "video", StringComparison.OrdinalIgnoreCase)) AddCategory(categories, "视频壁纸");
        if (string.Equals(Type, "web", StringComparison.OrdinalIgnoreCase)) AddCategory(categories, "网页壁纸");
        return categories;
    }

    private static bool ContainsAnimatedImage(string folder)
    {
        try
        {
            string root = Path.GetFullPath(folder);
            return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(Path.GetFileName(path), "preview.png", StringComparison.OrdinalIgnoreCase))
                .Where(IsImageFile)
                .Any(IsAnimatedImageFile);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".png" or ".webp" or ".apng";

    private static bool IsAnimatedImageFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".gif")
        {
            try
            {
                using var image = System.Drawing.Image.FromFile(path);
                return image.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time) > 1;
            }
            catch
            {
                return false;
            }
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (extension == ".webp") return ContainsAscii(bytes, "ANIM");
        return extension == ".apng" || ContainsAscii(bytes, "acTL");
    }

    private static bool ContainsAscii(byte[] bytes, string value)
    {
        byte[] marker = System.Text.Encoding.ASCII.GetBytes(value);
        for (int index = 0; index <= bytes.Length - marker.Length; index++)
        {
            if (bytes.AsSpan(index, marker.Length).SequenceEqual(marker)) return true;
        }
        return false;
    }

    private static void AddCategory(List<string> categories, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category) && !categories.Contains(category, StringComparer.CurrentCultureIgnoreCase)) categories.Add(category);
    }

    private static bool IsDerivedCategory(string? category) => category is not null &&
        (category.Equals("静态图片", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("动态图片", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("图片壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("静态壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("动态壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("视频壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("网页壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("可交互壁纸", StringComparison.CurrentCultureIgnoreCase) ||
         category.Equals("音频响应", StringComparison.CurrentCultureIgnoreCase));

    public static WallpaperManifest Load(string folder)
    {
        string path = Path.Combine(folder, "manifest.json");
        if (!File.Exists(path)) throw new FileNotFoundException("目录中缺少 manifest.json", path);
        var value = JsonSerializer.Deserialize<WallpaperManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("manifest.json 内容无效");
        if (string.IsNullOrWhiteSpace(value.Entry)) throw new InvalidDataException("manifest.json 的 Entry 不能为空");
        string root = Path.GetFullPath(folder);
        string full = Path.GetFullPath(Path.Combine(root, value.Entry));
        if (!IsInside(root, full)) throw new InvalidDataException("Entry 不能引用壁纸目录之外的文件");
        if (!File.Exists(full)) throw new FileNotFoundException("找不到壁纸入口文件", full);
        if (value.RecommendedFps is < 1 or > 240) throw new InvalidDataException("RecommendedFps 必须在 1 到 240 之间");
        if (!string.IsNullOrWhiteSpace(value.Preview))
        {
            string preview = Path.GetFullPath(Path.Combine(root, value.Preview));
            if (!IsInside(root, preview)) throw new InvalidDataException("Preview 不能引用壁纸目录之外的文件");
            if (!File.Exists(preview)) throw new FileNotFoundException("找不到壁纸预览图", preview);
        }
        return value;
    }

    private static bool IsInside(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
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
