# DeskCanvas

测试
Windows-only interactive desktop wallpaper workbench built with C# .NET 8 WinForms and WebView2. The app starts in the notification area without changing the desktop wallpaper. Left-click the tray icon to open the wallpaper library and navigation workbench.

## Project structure

```text
DeskCanvas/
  DeskCanvas.csproj
  Program.cs
  app.manifest
  Interop/NativeMethods.cs          # Win32 P/Invoke
  Models/WallpaperManifest.cs       # wallpaper package manifest
  Services/DesktopHost.cs           # Progman/WorkerW lookup
  Services/MouseForwarder.cs        # low-level mouse hook
  Services/WallpaperManager.cs      # monitor/window lifecycle
  UI/WallpaperWindow.cs             # WebView2 wallpaper window
  UI/SettingsForm.cs                # wallpaper library and navigation workbench
  UI/TrayApplicationContext.cs       # tray resident host
  Wallpapers/Particle/manifest.json
  Wallpapers/Particle/index.html    # built-in interactive particle sample
```

## NuGet

- `Microsoft.Web.WebView2` 1.0.2903.40

The target is `net8.0-windows`, `win-x86`, self-contained and single-file publish is enabled in the project. The Evergreen WebView2 Runtime is still required on the target machine; self-contained .NET does not bundle the browser runtime. The application architecture must match the installed WebView2 Runtime architecture.

## Build and run

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

Publish a single executable:

```powershell
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
.\publish\DeskCanvas.exe
```

## Test operations

1. Start the program. It stays in the tray and does not replace the current wallpaper automatically.
2. Left-click the tray icon to open the workbench. Use the wallpaper library to select a package and apply it to every monitor.
3. Use `创建 / 导入` to import a folder containing `manifest.json`, or import an image/video file directly. Direct media imports are converted into a local wallpaper package automatically.
4. Move the mouse over the desktop after applying the built-in particle wallpaper. Input is throttled and the particle/connection counts are capped to avoid runaway rendering.
5. Use tray pause/resume and close wallpaper. Closing the workbench leaves wallpapers and the tray process running.
6. Start a maximized/fullscreen application. With automatic pause enabled, wallpaper rendering is hidden; when focus returns to the desktop, it is restored.

A wallpaper package can be as small as:

```text
MyWallpaper/
  manifest.json
  index.html
```

```json
{
  "id": "my-wallpaper",
  "title": "My Wallpaper",
  "type": "web",
  "category": "可交互壁纸",
  "tags": ["科技", "互动"],
  "entry": "index.html",
  "interactive": true
}
```

HTML, CSS, JavaScript, Canvas, Three.js files, local images and local MP4 video can be referenced by the entry page. The package validator prevents `entry` from escaping the package directory.

## Known pitfalls

- WebView2 Runtime is a machine prerequisite. Install the Evergreen Runtime on clean Windows 10/11 systems. The Runtime is installed per architecture, so a 32-bit build needs the 32-bit Runtime (`dotnet run` with `RuntimeIdentifier=win-x86` requires a self-contained build, because only the x64 .NET Desktop Runtime may be installed).
- **Two mounting modes.** DeskCanvas probes for the classic desktop layout (a top-level `WorkerW` sitting behind the `SHELLDLL_DefView` icon layer, created by the documented `0x052C` Progman message). When that layer exists (Windows 10 and older Windows 11 builds) the wallpaper window is attached behind the desktop icons, so the icons stay usable.
- **Cover mode on Windows 11 25H2 and newer.** Those builds expose only `Progman → SHELLDLL_DefView → SysListView32`; there is no separate background layer, and windows attached inside the desktop hierarchy are not composited — a mounted WebView2 stays invisible there (verified on build 26200: attaching to `Progman`, to `WorkerW`, or inside `SHELLDLL_DefView` all render nothing, while the same page renders correctly in a normal window). DeskCanvas therefore falls back to a full-screen borderless window kept at the bottom of the Z order (`WS_EX_NOACTIVATE`, ignored `SC_MINIMIZE` so 显示桌面/Win+D cannot hide it, periodically restored by the tray timer). The wallpaper is then visible, but **desktop icons are covered** — close the wallpaper (tray ▸ 关闭壁纸, or 显示与性能 ▸ 关闭当前壁纸) to get the icons back. The workbench states this in the status line and warns once after the first apply.
- Applying is best effort per monitor: a display that cannot host a WebView2 window (virtual or wireless displays, disconnectable projectors) is reported in the status line and the log instead of aborting the whole apply. Desktop coordinate for such displays should be non-negative.
- A WebView2 environment becomes unusable when the Runtime is updated while the app runs, or when the browser process exits. `CreateCoreWebView2ControllerAsync` then fails with `0x8007139F` (ERROR_INVALID_STATE); the manager discards the cached environment and retries the affected monitor with a fresh environment, and mounted windows are reused (only navigated) so switching wallpapers never re-creates the WebView2.
- A low-level mouse hook observes global mouse input and the application forwards synthetic DOM events. This keeps the wallpaper interactive while desktop hit-testing remains available, but it is not a security boundary and some elevated applications may restrict cross-integrity interaction.
- The fullscreen check detects a non-DeskCanvas foreground window whose bounds cover a monitor, ignoring shell windows (desktop, taskbar, IME) and DWM-cloaked windows. It is a practical GPU-saving heuristic, not a guaranteed detector for every exclusive-D3D presentation mode.
- Desktop coordinates can be negative on a monitor arranged left or above the primary monitor. The implementation uses each `Screen.Bounds` directly.
- Video playback requires the codecs available to WebView2/Windows. Autoplay policies may require muted playback or a user gesture.
- The current sample forwards mouse movement, left-button and right-button events. Keyboard focus is intentionally not captured so normal desktop shortcuts continue to work.
