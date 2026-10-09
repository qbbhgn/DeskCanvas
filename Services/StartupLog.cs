namespace DeskCanvas.Services;

/// <summary>记录启动阶段异常，避免 WinExe 进程在双击启动时静默失败。</summary>
internal static class StartupLog
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskCanvas", "startup.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // 日志不能反过来阻止应用启动。
        }
    }

    public static string GetPath() => FilePath;
}
