using System.IO;
using VideoShelf.Models;

namespace VideoShelf.Services;

public sealed class VideoScanner
{
    private readonly QuickScanner _scanner = new();

    public int Count(string folder) => _scanner.ScanFiles(folder).Count;

    // 扫描只负责发现文件，耗时的缩略图和预览由后台处理。
    public async IAsyncEnumerable<VideoInfo> ScanAsync(string folder)
    {
        foreach (string file in _scanner.ScanFiles(folder))
        {
            var fileInfo = new FileInfo(file);
            yield return new VideoInfo
            {
                FilePath = file,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                CreatedTime = fileInfo.CreationTime,
                IsLoading = true
            };

            // 定期把控制权还给 UI，使卡片逐个出现。
            await Task.Yield();
        }
    }
}
