# DeskCanvas

DeskCanvas 是一个仅支持 Windows 的交互式桌面壁纸工作台，使用 C# .NET 8 WinForms 和 WebView2 构建。应用启动后常驻通知区域，不会自动更改当前桌面壁纸。左键单击托盘图标即可打开壁纸库和管理工作台。

## 项目结构

```text
DeskCanvas/
  DeskCanvas.csproj
  Program.cs
  app.manifest
  Interop/NativeMethods.cs          # Win32 P/Invoke
  Models/WallpaperManifest.cs       # 壁纸包清单
  Services/DesktopHost.cs           # 查找 Progman/WorkerW
  Services/MouseForwarder.cs        # 低级鼠标钩子
  Services/WallpaperManager.cs      # 显示器/窗口生命周期
  UI/WallpaperWindow.cs             # WebView2 壁纸窗口
  UI/SettingsForm.cs                # 壁纸库和管理工作台
  UI/TrayApplicationContext.cs       # 托盘驻留宿主
  Wallpapers/Particle/manifest.json
  Wallpapers/Particle/index.html    # 内置交互式粒子示例
```

## NuGet

- `Microsoft.Web.WebView2` 1.0.2903.40

项目目标框架为 `net8.0-windows`，运行时为 `win-x86`，并已启用自包含和单文件发布。目标机器仍需安装 Evergreen WebView2 Runtime；自包含 .NET 不会打包浏览器运行时。应用架构必须与已安装的 WebView2 Runtime 架构一致。

## 构建和运行

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

发布单个可执行文件：

```powershell
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
.\publish\DeskCanvas.exe
```

## 测试操作

1. 启动程序。程序会常驻托盘，不会自动替换当前桌面壁纸。
2. 左键单击托盘图标打开工作台。在壁纸库中选择一个壁纸包，并将其应用到所有显示器。
3. 使用 `创建 / 导入` 导入包含 `manifest.json` 的文件夹，或直接导入图片/视频文件。直接导入的媒体文件会自动转换为本地壁纸包。
4. 应用内置粒子壁纸后，将鼠标移动到桌面上。输入事件会进行节流，粒子数和连线数也会受到限制，以避免渲染失控。
5. 使用托盘菜单暂停、恢复或关闭壁纸。关闭工作台不会停止壁纸和托盘进程。
6. 启动最大化或全屏应用。启用自动暂停后，壁纸渲染会被隐藏；焦点返回桌面后，壁纸会自动恢复。

一个壁纸包最少可以包含以下文件：

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

入口页面可以引用 HTML、CSS、JavaScript、Canvas、Three.js 文件、本地图片和本地 MP4 视频。壁纸包验证器会阻止 `entry` 路径跳出壁纸包目录。

## 已知问题和注意事项

- WebView2 Runtime 是运行本程序的机器前置条件。在全新安装的 Windows 10/11 系统上，请安装 Evergreen Runtime。Runtime 按架构分别安装，因此 32 位构建需要 32 位 Runtime（使用 `RuntimeIdentifier=win-x86` 的 `dotnet run` 需要自包含构建，因为系统可能只安装了 x64 .NET Desktop Runtime）。
- **两种挂载模式。** DeskCanvas 会探测经典桌面布局（顶层 `WorkerW` 位于 `SHELLDLL_DefView` 图标层之后，由文档记录的 `0x052C` Progman 消息创建）。当该图层存在时（Windows 10 和较早版本的 Windows 11），壁纸窗口会挂载在桌面图标之后，因此图标仍然可以正常使用。
- **Windows 11 25H2 及更高版本的覆盖模式。** 这些版本只提供 `Progman → SHELLDLL_DefView → SysListView32`，没有独立的背景层；挂载在桌面层级内部的窗口不会参与合成，因此挂载的 WebView2 会保持不可见（已在版本 26200 上验证：挂载到 `Progman`、`WorkerW` 或 `SHELLDLL_DefView` 内部都没有任何渲染，而同一页面在普通窗口中可以正常显示）。因此 DeskCanvas 会退回到一个全屏无边框窗口，并将其保持在 Z 序底部（使用 `WS_EX_NOACTIVATE`，忽略 `SC_MINIMIZE`，所以显示桌面/Win+D 无法将其隐藏；托盘计时器会定期恢复该窗口）。此时壁纸可见，但**会覆盖桌面图标**。关闭壁纸（托盘 ▸ 关闭壁纸，或显示与性能 ▸ 关闭当前壁纸）即可恢复图标。工作台会在状态栏中显示提示，并在首次应用后发出一次警告。
- 壁纸应用会按显示器尽力执行：无法承载 WebView2 窗口的显示器（虚拟显示器、无线显示器或可断开的投影仪）会被记录在状态栏和日志中，而不会导致整个应用过程终止。这类显示器的桌面坐标应为非负数。
- 如果程序运行期间 WebView2 Runtime 被更新，或浏览器进程退出，WebView2 环境可能会失效。此时 `CreateCoreWebView2ControllerAsync` 会因 `0x8007139F`（ERROR_INVALID_STATE）失败；管理器会丢弃缓存的环境，为受影响的显示器创建新环境并重试，同时复用已挂载的窗口（只重新导航），因此切换壁纸不会重新创建 WebView2。
- 低级鼠标钩子会观察全局鼠标输入，应用则转发合成的 DOM 事件。这使壁纸保持交互能力，同时保留桌面命中测试，但它不是安全边界，某些高权限应用可能会限制跨完整性级别的交互。
- 全屏检测会查找一个非 DeskCanvas 的前台窗口，判断其边界是否覆盖某个显示器，同时忽略 Shell 窗口（桌面、任务栏、IME）和被 DWM 隐藏的窗口。这是用于节省 GPU 资源的实用启发式方案，并不能保证检测到所有独占式 D3D 呈现模式。
- 如果显示器排列在主显示器的左侧或上方，桌面坐标可能为负数。实现会直接使用每个 `Screen.Bounds` 的坐标。
- 视频播放需要 WebView2/Windows 提供相应的编解码器。自动播放策略可能要求视频静音，或要求用户先进行交互。
- 当前示例会转发鼠标移动、鼠标左键和右键事件。程序有意不捕获键盘焦点，以便常规桌面快捷键继续工作。
