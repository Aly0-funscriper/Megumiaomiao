# Megumiaomiao

Megumiaomiao is a Windows media-library player built with .NET 8 and WPF. It embeds mpv, exposes JSON IPC for MultiFunPlayer (MFP), and keeps large local video and audio collections easy to browse.

## Highlights

- Explorer-style folder view with a folder tree, breadcrumb path, Back and Up navigation
- Cached static thumbnails and optional lightweight animated previews
- Embedded or external mpv playback with seeking, fullscreen, A–B loop and keyboard controls
- MultiFunPlayer-compatible mpv IPC and same-folder `.funscript` discovery
- Video and audio library support, including embedded MP3 album artwork and LRC lyrics
- Favorites, persistent playlist, collections, shuffle playback, search and multi-select actions
- Chinese and English interfaces and user guides
- Shared, configurable cache directory designed to survive application upgrades

## Requirements

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `mpv.exe`
- `ffmpeg.exe` for metadata, thumbnails and previews

Place `ffmpeg.exe` in `Tools` before building. At runtime, select your preferred `mpv.exe` from Settings. Large third-party binaries are intentionally excluded from source control.

## Build

```powershell
dotnet restore
dotnet build VideoShelf.csproj -c Release
```

## MFP

The default IPC pipe is `\\.\pipe\multifunplayer-mpv`. When MFP starts Megumiaomiao through the compatibility executable, command-line `--input-ipc-server` values are honored so different MFP versions can provide their own pipe name.

For best results, start playback in Megumiaomiao and then connect MFP to the MPV source. Media is opened from its original absolute path, allowing MFP to locate a matching `.funscript` beside the media file.

## Author

Aly0 — generated with ChatGPT.

---

Megumiaomiao 是一款基于 .NET 8/WPF 的 Windows 本地媒体库播放器，支持内嵌 mpv、MFP IPC、缩略图缓存、文件夹浏览、收藏夹、播放列表、合集、搜索与多选管理。详细操作请参阅 `使用说明-中文.txt`。
