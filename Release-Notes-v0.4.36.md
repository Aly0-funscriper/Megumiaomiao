# Megumiaomiao v0.4.36

## 中文

- 视频卡片按滚动位置分批增量渲染，降低大型媒体库启动时的 UI 阻塞。
- 缩略图任务移出 UI 上下文，并节流高频进度更新。
- 批量排除短媒体改为集中处理，减少大型媒体库中的重复遍历。
- 刷新列表时只停止当前显示卡片的动态预览。
- 识别普通同名 `.funscript`，以及 `<视频名>.Lnip.funscript`、`<视频名>.Rnip.funscript` 侧车脚本。
- funscript 筛选和缺失数量统计将 Lnip/Rnip 任一脚本视为有效匹配。
- 时间轴可同时绘制普通、Lnip 和 Rnip 轨迹；播放继续使用视频原始绝对路径，便于 MFP 自动查找脚本。
- 仓库排除构建产物、缓存、个人配置、临时文件和大型第三方可执行文件。

## English

- Video cards render incrementally near the scroll position, reducing UI blocking for large libraries.
- Thumbnail work runs outside the UI context and high-frequency progress updates are throttled.
- Bulk short-media exclusions are processed in one batch to avoid repeated large-list traversal.
- List refresh stops hover previews only for currently displayed cards.
- Recognizes standard same-name `.funscript` files and `<video>.Lnip.funscript` / `<video>.Rnip.funscript` sidecars.
- Funscript filtering and missing-script counts treat either Lnip or Rnip as a valid match.
- The timeline can display standard, Lnip and Rnip tracks together while playback keeps the original absolute media path for MFP discovery.
- The repository excludes build output, caches, personal configuration, temporary files and large third-party executables.

## Validation

- `dotnet restore VideoShelf.csproj` — passed.
- `dotnet build VideoShelf.csproj -c Release` — passed with 0 warnings and 0 errors on Windows 11 x64 with .NET SDK 10.0.302 and .NET 8 Windows Desktop Runtime installed.
- mpv/MFP live interoperability was not executed in this environment and should be verified on the target machine.
