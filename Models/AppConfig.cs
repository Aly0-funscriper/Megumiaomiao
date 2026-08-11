namespace VideoShelf.Models;

public sealed class AppConfig
{
    public string MpvPath { get; set; } = "";
    public bool UseEmbeddedPlayer { get; set; } = true;
    public bool EnableHoverPreview { get; set; } = false;
    public bool UseEnglish { get; set; } = false;
    public string CacheDirectory { get; set; } = "";
    public string IpcPipeName { get; set; } = "multifunplayer-mpv";
    public double MinimumFileSizeMb { get; set; }
    public double MinimumDurationSeconds { get; set; }
}
