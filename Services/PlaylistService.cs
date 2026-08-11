using System.IO;
using System.Text.Json;

namespace VideoShelf.Services;

public sealed class PlaylistService
{
    private readonly string path = Path.Combine(StoragePaths.Root, "Playlist.json");

    public List<string> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(IEnumerable<string> paths)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
