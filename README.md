# Megumiaomiao

Megumiaomiao is a Windows media-library manager and player built with WPF. It uses mpv for playback and is designed to work with MultiFunPlayer (MFP) through mpv IPC while keeping the original media path available for sidecar scripts.

## 中文介绍

Megumiaomiao 是一款面向 Windows 的本地视频与音频管理播放器。它使用 WPF 构建，以 mpv 负责播放，并为 MultiFunPlayer（MFP）保留兼容的 mpv 入口和 IPC 连接方式。

### 主要功能

- 类似 Windows 文件资源管理器的文件夹浏览：目录树、面包屑、后退和上一级导航
- 静态缩略图缓存、可选的低负载动态预览和增量扫描
- 内嵌或独立 mpv 播放，支持拖动进度、全屏、A–B 循环和键盘控制
- 支持视频及常见音频格式，包含 MP3 内嵌封面和 LRC 歌词
- 收藏夹、持久化播放列表、合集、随机播放、搜索和 Ctrl 多选
- funscript 筛选：普通同名脚本，以及 `<视频名>.Lnip.funscript` / `<视频名>.Rnip.funscript` 侧车脚本
- 时间轴可同时显示普通、Lnip 和 Rnip 轨迹；播放时仍使用视频原始绝对路径
- 中英文界面和使用说明，缓存目录可配置并可跨版本复用

### 安装与运行

Windows x64 Release 包：

1. 下载 GitHub Releases 中的 `Megumiaomiao-v*-win-x64.zip`。
2. 解压到普通文件夹，不要直接在压缩包内运行。
3. 双击 `VideoShelf.exe` 启动当前兼容版本；在设置中选择真正的 `mpv.exe`。
4. 使用 MFP 兼容模式时，按下方说明选择发布包里的 `mpv.exe` 入口。

运行环境：

- Windows 10/11 x64
- .NET 8 Desktop Runtime（Release 包为框架依赖式发布）
- `ffmpeg.exe` 用于媒体信息、缩略图和预览；官方/可信 Windows 构建随发布包提供
- 真正的 `mpv.exe`，或者发布包中的 `real-mpv.exe`

### mpv 配置

第一次播放时，在设置中选择真正的 mpv 播放器。不要把 Megumiaomiao 的 MFP 兼容入口当作真正播放器选择。发布包中：

- `VideoShelf.exe`：Megumiaomiao 主程序
- `mpv.exe`：供 MFP 选择的兼容入口
- `real-mpv.exe`：实际播放的 mpv

### MultiFunPlayer 兼容说明

在 MFP 的 MPV 媒体源设置中，将 Executable 指向发布包内的 `mpv.exe`，并保留同目录文件。普通双击启动使用独立 IPC，不会抢占 MFP 的管道；由 MFP 启动时，程序会接受命令行传入的 `--input-ipc-server`。

建议先在 Megumiaomiao 中开始播放，再连接 MFP 的 MPV 媒体源。程序使用媒体原始绝对路径，因此 MFP 可以继续查找媒体旁边的：

```text
Video.mp4
Video.funscript
Video.Lnip.funscript
Video.Rnip.funscript
```

### 从源码构建

仓库源码保留 `VideoShelf` 项目文件和命名空间，以兼容现有工程；对外名称统一为 Megumiaomiao。

```powershell
dotnet restore
dotnet build VideoShelf.csproj -c Release
```

源码仓库不会提交 `bin/`、`obj/`、`.vs/`、用户缓存、个人路径或大型第三方可执行文件。若要构建发布包，请将可信来源的 `ffmpeg.exe` 放入 `Tools` 文件夹，再执行：

```powershell
dotnet publish VideoShelf.csproj -c Release -r win-x64 --self-contained false
```

### 开发状态

当前发布目标为 `v0.4.36`。已完成的主要方向包括媒体库管理、缓存与增量扫描、mpv 播放、MFP 兼容、文件夹浏览、合集/播放列表和 funscript 侧车识别。mpv 与 MFP 的实际联动仍取决于用户本机版本、路径和配置，发布前请在目标机器上验证。

---

## English Introduction

Megumiaomiao is a Windows video and audio library manager built with WPF. It uses mpv for playback and preserves a MultiFunPlayer-compatible mpv entry point and IPC flow.

### Features

- Explorer-style folder browsing with a tree, breadcrumbs, Back and Up navigation
- Cached static thumbnails, optional lightweight hover previews and incremental scanning
- Embedded or external mpv playback with seeking, fullscreen, A–B looping and keyboard controls
- Video and common audio formats, including MP3 embedded artwork and LRC lyrics
- Favorites, persistent playlist, collections, shuffle, search and Ctrl multi-select
- Funscript filtering for standard same-name scripts and `<video>.Lnip.funscript` / `<video>.Rnip.funscript` sidecars
- A timeline that can display standard, Lnip and Rnip tracks together while keeping the original media path for MFP
- Chinese and English UI and guides, with a configurable cache that can be reused across versions

### Installation and running

For Windows x64:

1. Download `Megumiaomiao-v*-win-x64.zip` from GitHub Releases.
2. Extract it to a normal folder; do not run from inside the ZIP.
3. Launch `VideoShelf.exe` and select the real `mpv.exe` in Settings.
4. For MFP compatibility mode, use the `mpv.exe` entry described below.

Requirements:

- Windows 10/11 x64
- .NET 8 Desktop Runtime (the Release package is framework-dependent)
- `ffmpeg.exe` for media probing, thumbnails and previews
- A real `mpv.exe`, or the `real-mpv.exe` included in the package

### mpv configuration

Choose the real mpv player on first use. Do not select the Megumiaomiao MFP compatibility entry as the real player. In the release package:

- `VideoShelf.exe` is the Megumiaomiao application
- `mpv.exe` is the compatibility entry for MFP
- `real-mpv.exe` is the actual mpv player

### MultiFunPlayer compatibility

In MFP's MPV media-source settings, point Executable to the package's `mpv.exe` and keep the package files together. A normal standalone launch uses a private IPC pipe; an instance started by MFP accepts its `--input-ipc-server` argument.

For best results, start playback in Megumiaomiao and then connect MFP to the MPV source. The application opens the original absolute media path, allowing MFP to find sidecars such as:

```text
Video.mp4
Video.funscript
Video.Lnip.funscript
Video.Rnip.funscript
```

### Build from source

The repository keeps the `VideoShelf` project file and namespace for compatibility; the public product name is Megumiaomiao.

```powershell
dotnet restore
dotnet build VideoShelf.csproj -c Release
```

`bin/`, `obj/`, `.vs/`, local caches, personal paths and large third-party executables are excluded from source control. To publish locally, place a trusted Windows build of `ffmpeg.exe` in `Tools`, then run:

```powershell
dotnet publish VideoShelf.csproj -c Release -r win-x64 --self-contained false
```

### Development status

The current release target is `v0.4.36`. The project covers media-library management, caching and incremental scanning, mpv playback, MFP compatibility, folder browsing, collections/playlists and funscript sidecar matching. Live mpv/MFP interoperability still depends on the user's local versions, paths and configuration and should be verified on the target machine.

## Author / 作者

Aly0 — generated with ChatGPT / 使用 ChatGPT 生成。
