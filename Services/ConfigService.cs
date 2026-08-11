using System;
using System.IO;
using System.Text.Json;
using VideoShelf.Models;

namespace VideoShelf.Services;

public sealed class ConfigService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoShelf", "config.json");

    public AppConfig Load()
    {
        try
        {
            var config = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? new AppConfig()
                : new AppConfig();
            // MFP's MPV media source uses this fixed pipe name and does not expose it in settings.
            if (string.IsNullOrWhiteSpace(config.IpcPipeName) ||
                config.IpcPipeName.Equals("VideoShelf-mpv", StringComparison.OrdinalIgnoreCase))
            {
                config.IpcPipeName = "multifunplayer-mpv";
                Save(config);
            }
            return config;
        }
        catch { return new AppConfig(); }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
