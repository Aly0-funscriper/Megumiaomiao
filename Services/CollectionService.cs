using System.Text.Json;
using System.IO;
using VideoShelf.Models;
namespace VideoShelf.Services;
public sealed class CollectionService
{
    private readonly string path = Path.Combine(StoragePaths.Root, "Collections.json");
    public List<MediaCollection> Load() { try { return File.Exists(path) ? JsonSerializer.Deserialize<List<MediaCollection>>(File.ReadAllText(path)) ?? new() : new(); } catch { return new(); } }
    public void Save(List<MediaCollection> value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
