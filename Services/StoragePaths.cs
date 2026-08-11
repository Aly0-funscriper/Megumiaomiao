using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VideoShelf.Services;

internal static class StoragePaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoShelf");
    public static string CacheFile { get; } = Path.Combine(Root, "VideoCache.json");
    public static string LibraryCaches { get; } = Path.Combine(Root, "Libraries");
    public static string AssetRoot { get; private set; } = Path.Combine(Root, "Cache");
    public static string Thumbnails => Path.Combine(AssetRoot, "Thumbnails");
    public static string Previews => Path.Combine(AssetRoot, "Previews");

    public static void Configure(string? configuredDirectory, string? videoRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            AssetRoot = Path.GetFullPath(configuredDirectory);
        else if (!string.IsNullOrWhiteSpace(videoRoot) && Directory.Exists(videoRoot))
            AssetRoot = Path.Combine(videoRoot, ".VideoShelfCache");
        EnsureCreated();
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LibraryCaches);
        Directory.CreateDirectory(AssetRoot);
        Directory.CreateDirectory(Thumbnails);
        Directory.CreateDirectory(Previews);
    }

    public static string GetLibraryCacheFile(string rootFolder)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootFolder)).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        return Path.Combine(LibraryCaches, hash + ".json");
    }
}
