# DeskCanvas

DeskCanvas 是一个 Windows 交互式桌面壁纸工作台，支持网页、图片和视频壁纸。程序常驻通知区域，左键单击托盘图标即可打开壁纸库和管理工作台。

## 最新下载地址

[点击下载最新版本](https://github.com/qbbhgn/DeskCanvas/releases/latest/download/DeskCanvas.zip)

## 当前功能

- **壁纸库管理**：浏览、搜索和按分类筛选壁纸，查看壁纸信息，删除导入的壁纸。
- **创建与导入**：支持导入网页壁纸文件夹，以及 JPG、PNG、WebP、MP4、WebM 等常见图片和视频文件；导入媒体时自动生成壁纸包，网页壁纸缺少清单时可在导入前补充配置。
- **多显示器支持**：可将壁纸应用到所有显示器，也可为指定显示器设置独立壁纸。
- **交互与音频响应**：支持鼠标交互；具备音频响应能力的壁纸可随系统播放的声音变化。
- **壁纸属性调节**：在“显示与性能”中调整壁纸提供的参数，并保存设置。
- **运行控制**：通过托盘菜单暂停、恢复或关闭壁纸；关闭工作台后，壁纸仍可继续运行。
- **自动暂停**：可启用全屏应用运行时自动暂停壁纸，返回桌面后恢复播放。
- **自动轮播**：可选择播放列表，设置 1 到 1440 分钟的轮换间隔，并立即切换到下一张壁纸；空列表使用壁纸库中的全部有效项目，显示器专属壁纸不受全局轮播影响。
- **开机启动**：可启用开机自动启动并恢复壁纸。

## 自带壁纸

### 交互粒子

交互式粒子与连线效果，支持鼠标交互和系统音频响应。可调整粒子数量，默认 150 个，范围为 30 到 400 个。

### 桌面苍蝇

透明叠加在桌面上的苍蝇生态，围绕桌面图标活动，支持拍打和清扫。

- 按住 `Alt` 显示苍蝇拍，鼠标左键拍打附近区域；每只苍蝇命中两次后死亡。
- 按住 `Ctrl` 显示扫把，鼠标左键清扫卵和尸体；各自清扫两次后消失。同时按住两个键时优先清扫。
- 可调整苍蝇数量上限、生成间隔和苍蝇大小；默认数量上限为 30，生成间隔为 5 分钟。

### LOL信号

带音效的半透明桌面信号轮。按住 `Alt` 或 `Ctrl` 打开信号轮，移动鼠标选择信号，松开按键发送。

## 构建和运行

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

发布单个可执行文件：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
.\publish\DeskCanvas.exe
```

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

## 📄 开源协议

本项目采用 CC BY-NC 4.0（知识共享-署名-非商业性使用 4.0 国际）协议。

> 您可以自由使用、修改、分享本项目，但**不得用于商业目的**，且需保留原作者署名。

## 👤 作者

**添碗**
