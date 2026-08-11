# Megumiaomiao

## 中文介绍

Megumiaomiao 是一款基于 .NET 8 与 WPF 开发的 Windows 本地媒体库播放器。软件以 mpv 为播放核心，支持与 MultiFunPlayer（MFP）通过 JSON IPC 连接，适合管理数量较多的视频与音频作品。

### 主要功能

- 类似 Windows 文件资源管理器的文件夹浏览：目录树、面包屑路径、后退与上一级导航
- 静态缩略图缓存，以及可自行开启的低负载动态预览
- 内嵌或独立 mpv 播放，支持进度跳转、拖动、全屏、A–B 循环与键盘控制
- 兼容 MultiFunPlayer 的 mpv IPC，并识别媒体同目录下的 `.funscript`
- 支持视频与多种音频格式、MP3 内嵌专辑封面和 LRC 歌词
- 收藏夹、持久化播放列表、合集、随机播放、搜索与 Ctrl 多选管理
- 中英文界面及中英文使用说明
- 可配置、跨版本复用的缓存目录

### 运行要求

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `mpv.exe`
- 用于读取信息和生成预览的 `ffmpeg.exe`

使用 Release 压缩包时，解压后运行程序，并在设置中选择需要使用的 `mpv.exe`。不要直接在压缩包内运行。

### MFP 使用说明

默认 IPC 管道为 `\\.\pipe\multifunplayer-mpv`。当 MFP 通过兼容入口启动 Megumiaomiao 时，程序会采用命令行传入的 `--input-ipc-server`，以兼容不同 MFP 版本提供的管道名称。

建议先在 Megumiaomiao 中播放媒体，再连接 MFP 的 MPV 媒体源。程序会用原始绝对路径打开文件，因此 MFP 可以继续寻找媒体旁边同名的 `.funscript`。

### 从源码构建

将可信来源的 `ffmpeg.exe` 放入 `Tools` 文件夹，然后运行：

```powershell
dotnet restore
dotnet build VideoShelf.csproj -c Release
```

大型第三方可执行文件不会提交到源码仓库。

---

## English Introduction

Megumiaomiao is a Windows media-library player built with .NET 8 and WPF. It uses mpv for playback, exposes JSON IPC for MultiFunPlayer (MFP), and is designed to keep large local video and audio collections easy to browse.

### Highlights

- Explorer-style folder view with a folder tree, breadcrumb path, Back and Up navigation
- Cached static thumbnails and optional lightweight animated previews
- Embedded or external mpv playback with seeking, dragging, fullscreen, A–B loop and keyboard controls
- MultiFunPlayer-compatible mpv IPC and same-folder `.funscript` discovery
- Video and multi-format audio support, embedded MP3 album artwork and LRC lyrics
- Favorites, persistent playlist, collections, shuffle playback, search and Ctrl multi-select actions
- Chinese and English interfaces and user guides
- Shared, configurable cache directory designed to survive application upgrades

### Requirements

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `mpv.exe`
- `ffmpeg.exe` for metadata, thumbnails and previews

When using the Release package, extract it first, launch the application, and select your preferred `mpv.exe` in Settings. Do not run it directly inside the ZIP archive.

### MFP

The default IPC pipe is `\\.\pipe\multifunplayer-mpv`. When MFP starts Megumiaomiao through the compatibility entry point, command-line `--input-ipc-server` values are honored so different MFP versions can provide their own pipe name.

For best results, start playback in Megumiaomiao and then connect MFP to the MPV source. Media is opened from its original absolute path, allowing MFP to locate a matching `.funscript` beside the media file.

### Build from source

Place a trusted Windows build of `ffmpeg.exe` in `Tools`, then run:

```powershell
dotnet restore
dotnet build VideoShelf.csproj -c Release
```

Large third-party executables are intentionally excluded from source control.

## Author / 作者

Aly0 — generated with ChatGPT / 使用 ChatGPT 生成。
