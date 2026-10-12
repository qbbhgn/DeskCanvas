using Microsoft.Win32;

namespace DeskCanvas.Services;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskCanvas";

    public static void Configure(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                existingKey?.DeleteValue(ValueName, false);
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath)) return;

            string command = BuildCommand(processPath) + " --startup";
            if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
                key.SetValue(ValueName, command);
        }
        catch (Exception ex)
        {
            StartupLog.Write("注册开机启动失败，壁纸仍可正常运行", ex);
        }
    }

    public static void Disable() => Configure(false);

    private static string BuildCommand(string processPath)
    {
        string quotedProcess = $"\"{processPath}\"";
        string? entryAssembly = Environment.GetCommandLineArgs().FirstOrDefault();
        return Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(entryAssembly) &&
               !string.Equals(entryAssembly, processPath, StringComparison.OrdinalIgnoreCase)
            ? $"{quotedProcess} \"{entryAssembly}\""
            : quotedProcess;
    }
}