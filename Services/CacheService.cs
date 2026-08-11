using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VideoShelf.Models;

namespace VideoShelf.Services;

public sealed class CacheService
{
    public CacheService() => StoragePaths.EnsureCreated();

    public void Save(List<VideoInfo> videos, string rootFolder)
    {
        var cache = new VideoCache
        {
            RootFolder = rootFolder,
            Videos = videos,
            UpdateTime = DateTime.Now
        };
        File.WriteAllText(StoragePaths.CacheFile,
            JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        if (!string.IsNullOrWhiteSpace(rootFolder))
            File.WriteAllText(StoragePaths.GetLibraryCacheFile(rootFolder),
                JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
    }

    public VideoCache LoadCache()
    {
        string? source = File.Exists(StoragePaths.CacheFile) ? StoragePaths.CacheFile : FindLegacyCache();
        if (source == null) return new VideoCache();
        try
        {
            var cache = JsonSerializer.Deserialize<VideoCache>(File.ReadAllText(source)) ?? new VideoCache();
            bool migrated = !Path.GetFullPath(source).Equals(Path.GetFullPath(StoragePaths.CacheFile), StringComparison.OrdinalIgnoreCase);
            foreach (var video in cache.Videos)
            {
                migrated |= MigrateAsset(video.ThumbnailPath, StoragePaths.Thumbnails, path => video.ThumbnailPath = path);
                migrated |= MigrateAsset(video.PreviewPath, StoragePaths.Previews, path => video.PreviewPath = path);
            }
            if (migrated) Save(cache.Videos, cache.RootFolder);
            return cache;
        }
        catch { return new VideoCache(); }
    }

    public List<VideoInfo> Load() => LoadCache().Videos;

    public VideoCache LoadCacheForRoot(string rootFolder)
    {
        try
        {
            string libraryFile = StoragePaths.GetLibraryCacheFile(rootFolder);
            if (File.Exists(libraryFile)) return LoadFromFile(libraryFile);
            var current = LoadCache();
            if (Path.TrimEndingDirectorySeparator(Path.GetFullPath(current.RootFolder))
                .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootFolder)), StringComparison.OrdinalIgnoreCase))
            {
                Save(current.Videos, current.RootFolder);
                return current;
            }
        }
        catch { }
        return new VideoCache { RootFolder = rootFolder };
    }

    public void CleanupRemovedAssets(IEnumerable<VideoInfo> previous, IEnumerable<VideoInfo> current)
    {
        var active = current.Select(v => v.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in previous.Where(v => !active.Contains(v.FilePath)))
        {
            TryDeleteAsset(removed.ThumbnailPath);
            TryDeleteAsset(removed.PreviewPath);
        }
    }

    private VideoCache LoadFromFile(string source)
    {
        var cache = JsonSerializer.Deserialize<VideoCache>(File.ReadAllText(source)) ?? new VideoCache();
        bool migrated = false;
        foreach (var video in cache.Videos)
        {
            migrated |= MigrateAsset(video.ThumbnailPath, StoragePaths.Thumbnails, path => video.ThumbnailPath = path);
            migrated |= MigrateAsset(video.PreviewPath, StoragePaths.Previews, path => video.PreviewPath = path);
        }
        if (migrated) Save(cache.Videos, cache.RootFolder);
        return cache;
    }

    private static void TryDeleteAsset(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    public static string ReadCachedRootFolder()
    {
        try
        {
            if (!File.Exists(StoragePaths.CacheFile)) return "";
            return JsonSerializer.Deserialize<VideoCache>(File.ReadAllText(StoragePaths.CacheFile))?.RootFolder ?? "";
        }
        catch { return ""; }
    }

    public void MoveAssetsTo(string targetRoot, List<VideoInfo> videos, string libraryRoot)
    {
        string oldRoot = StoragePaths.AssetRoot;
        StoragePaths.Configure(targetRoot);
        foreach (var video in videos)
        {
            MoveAsset(video.ThumbnailPath, StoragePaths.Thumbnails, path => video.ThumbnailPath = path);
            MoveAsset(video.PreviewPath, StoragePaths.Previews, path => video.PreviewPath = path);
        }
        Save(videos, libraryRoot);
        TryDeleteEmpty(oldRoot);
    }

    private static bool MigrateAsset(string source, string targetFolder, Action<string> update)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return false;
        if (Path.GetDirectoryName(source)?.Equals(targetFolder, StringComparison.OrdinalIgnoreCase) == true) return false;
        try
        {
            string target = Path.Combine(targetFolder, Path.GetFileName(source));
            if (!File.Exists(target))
            {
                try { File.Move(source, target); }
                catch
                {
                    File.Copy(source, target);
                    try { File.Delete(source); } catch { }
                }
            }
            update(target);
            return true;
        }
        catch { return false; }
    }

    private static void MoveAsset(string source, string targetFolder, Action<string> update)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        if (Path.GetDirectoryName(source)?.Equals(targetFolder, StringComparison.OrdinalIgnoreCase) == true) return;
        try
        {
            string target = Path.Combine(targetFolder, Path.GetFileName(source));
            if (!File.Exists(target))
            {
                try { File.Move(source, target); }
                catch { File.Copy(source, target); File.Delete(source); }
            }
            update(target);
        }
        catch { }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(directory, true);
        }
        catch { }
    }

    private static string? FindLegacyCache()
    {
        var candidates = new List<string>();
        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VideoCache.json");
        if (File.Exists(local)) candidates.Add(local);
        try
        {
            string extractedRoot = Path.Combine(Path.GetTempPath(), ".net", "VideoShelf");
            if (Directory.Exists(extractedRoot))
                candidates.AddRange(Directory.EnumerateFiles(extractedRoot, "VideoCache.json", SearchOption.AllDirectories));
        }
        catch { }
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
}
