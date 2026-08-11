using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VideoShelf.Models;

namespace VideoShelf.Services;

public sealed class MpvService : IAsyncDisposable
{
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readerCancellation;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private double _position;
    private double _duration;
    private string _controlPipeName = "";

    public bool IsRunning => _process is { HasExited: false };
    public event EventHandler<PlaybackProgressEventArgs>? PlaybackProgressChanged;
    public event EventHandler<string>? MediaPathChanged;

    public async Task StartAsync(AppConfig config, string? mediaPath, IntPtr hostHandle, CancellationToken token = default, bool startPaused = false, bool exposeMfpPipe = false)
    {
        if (!File.Exists(config.MpvPath)) throw new FileNotFoundException("请先选择有效的 mpv.exe。", config.MpvPath);
        await StopAsync();

        var arguments = new StringBuilder("--no-config --idle=yes --keep-open=yes --force-window=yes --audio-exclusive=no --audio-client-name=VideoShelf ");
        _controlPipeName = $"VideoShelf-control-{Environment.ProcessId}";
        arguments.Append("--input-ipc-server=").Append(Quote(@"\\.\pipe\" + _controlPipeName)).Append(' ');
        if (config.UseEmbeddedPlayer && hostHandle != IntPtr.Zero)
            arguments.Append("--wid=").Append(hostHandle.ToInt64()).Append(' ');
        if (startPaused) arguments.Append("--pause=yes ");
        if (!string.IsNullOrWhiteSpace(mediaPath)) arguments.Append(Quote(mediaPath));

        _process = Process.Start(new ProcessStartInfo(config.MpvPath, arguments.ToString())
        {
            UseShellExecute = false,
            CreateNoWindow = config.UseEmbeddedPlayer,
            WorkingDirectory = Path.GetDirectoryName(config.MpvPath)!
        }) ?? throw new InvalidOperationException("mpv 启动失败。");

        _pipe = new NamedPipeClientStream(".", _controlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await _pipe.ConnectAsync(timeout.Token);
        _readerCancellation = new CancellationTokenSource();
        _ = ReadEventsAsync(_readerCancellation.Token);
        // A normally launched VideoShelf instance must not claim MFP's well-known pipe.
        // Only an instance actually launched by MFP is allowed to expose that endpoint.
        if (exposeMfpPipe && !string.IsNullOrWhiteSpace(config.IpcPipeName))
            await SendAsync("set_property", "input-ipc-server", @"\\.\pipe\" + config.IpcPipeName);
        await SendAsync("observe_property", 1, "time-pos");
        await SendAsync("observe_property", 2, "duration");
        await SendAsync("observe_property", 3, "path");
    }

    public Task LoadFileAsync(string path) => SendAsync("loadfile", path, "replace");
    public Task TogglePauseAsync() => SendAsync("cycle", "pause");
    public Task PlayAsync() => SendAsync("set_property", "pause", false);
    public Task StopPlaybackAsync() => SendAsync("stop");
    public Task ToggleFullscreenAsync() => SendAsync("cycle", "fullscreen");
    public Task SeekAbsoluteAsync(double seconds) => SendAsync("set_property", "time-pos", Math.Max(0, seconds));
    public Task SeekAsync(double seconds) => SendAsync("seek", seconds, "relative");
    public Task SetVolumeAsync(double volume) => SendAsync("set_property", "volume", Math.Clamp(volume, 0, 100));
    public Task SetAbLoopAsync(double? start, double? end) => SetAbLoopCoreAsync(start, end);

    private async Task SetAbLoopCoreAsync(double? start, double? end)
    {
        await SendAsync("set_property", "ab-loop-a", start is null ? "no" : start.Value);
        await SendAsync("set_property", "ab-loop-b", end is null ? "no" : end.Value);
    }

    public async Task SendAsync(params object[] command)
    {
        if (_pipe?.IsConnected != true) throw new InvalidOperationException("mpv IPC 尚未连接。");
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command }) + "\n");
        await _writeLock.WaitAsync();
        try { await _pipe.WriteAsync(payload); await _pipe.FlushAsync(); }
        finally { _writeLock.Release(); }
    }

    private async Task ReadEventsAsync(CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(_pipe!, Encoding.UTF8, false, 4096, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("event", out var eventName) || eventName.GetString() != "property-change") continue;
                if (!root.TryGetProperty("name", out var name) || !root.TryGetProperty("data", out var data)) continue;
                string? propertyName = name.GetString();
                if (propertyName == "path" && data.ValueKind == JsonValueKind.String)
                {
                    string? path = data.GetString();
                    if (!string.IsNullOrWhiteSpace(path)) MediaPathChanged?.Invoke(this, path);
                    continue;
                }
                if (data.ValueKind != JsonValueKind.Number) continue;
                if (propertyName == "time-pos") _position = data.GetDouble();
                if (propertyName == "duration") _duration = data.GetDouble();
                PlaybackProgressChanged?.Invoke(this, new PlaybackProgressEventArgs(_position, _duration));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async Task StopAsync()
    {
        if (_pipe?.IsConnected == true) { try { await SendAsync("quit"); } catch { } }
        _readerCancellation?.Cancel(); _readerCancellation?.Dispose(); _readerCancellation = null;
        _pipe?.Dispose(); _pipe = null;
        if (_process is { HasExited: false })
        {
            try
            {
                using var timeout = new CancellationTokenSource(500);
                await _process.WaitForExitAsync(timeout.Token);
            }
            catch { try { _process.Kill(true); } catch { } }
        }
        _process?.Dispose(); _process = null;
        _position = _duration = 0;
        PlaybackProgressChanged?.Invoke(this, new PlaybackProgressEventArgs(0, 0));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    public async ValueTask DisposeAsync() { await StopAsync(); _writeLock.Dispose(); }
}

public sealed record PlaybackProgressEventArgs(double Position, double Duration);
