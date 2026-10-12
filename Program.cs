using DeskCanvas.Services;
using DeskCanvas.UI;

namespace DeskCanvas;

internal static class Program
{
    internal static Icon AppIcon { get; private set; } = null!;

    // 单实例保证不会重复创建多套桌面窗口和全局鼠标钩子。
    [STAThread]
    private static void Main()
    {
        bool startupInvocation = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, "DeskCanvas.ShowSettings", out bool createdEvent);
        try
        {
            using var mutex = new Mutex(true, "DeskCanvas.Singleton", out bool created);
            if (!created)
            {
                if (!createdEvent) showSettings.Set();
                return;
            }
            ApplicationConfiguration.Initialize();
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("DeskCanvas.Assets.DeskCanvas.ico")
                ?? throw new InvalidOperationException("Application icon resource is missing.");
            using var icon = new Icon(iconStream);
            AppIcon = icon;
            Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupLog.Write("未处理的 AppDomain 异常", e.ExceptionObject as Exception);
            var manager = new WallpaperManager();
            StartupManager.Configure(manager.StartupEnabled);
            var app = new TrayApplicationContext(manager, showSettings, startupInvocation);
            Application.Run(app);
        }
        catch (Exception ex)
        {
            StartupLog.Write("程序启动失败", ex);
            MessageBox.Show($"DeskCanvas 启动失败。\n\n{ex.Message}\n\n日志：{StartupLog.GetPath()}", "DeskCanvas", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowFatalError(Exception exception)
    {
        StartupLog.Write("UI 线程异常", exception);
        MessageBox.Show($"DeskCanvas 运行异常。\n\n{exception.Message}\n\n日志：{StartupLog.GetPath()}", "DeskCanvas", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
