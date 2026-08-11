using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoShelf.Services;

public sealed class FFmpegService
{
    private readonly string _ffmpeg = ResolveTool("ffmpeg.exe");

    public async Task<string> GetVideoInfo(string path, CancellationToken cancellationToken = default)
    {
        string metadata = await ReadInputMetadataAsync(path, cancellationToken);
        var durationMatch = Regex.Match(metadata, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        double seconds = 0;
        if (durationMatch.Success)
            seconds = int.Parse(durationMatch.Groups[1].Value) * 3600
                + int.Parse(durationMatch.Groups[2].Value) * 60
                + double.Parse(durationMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var videoLine = metadata.Split('\n').FirstOrDefault(line => line.Contains("Video:", StringComparison.OrdinalIgnoreCase)) ?? "";
        var sizeMatch = Regex.Match(videoLine, @"(?<!\d)(\d{2,5})x(\d{2,5})(?!\d)", RegexOptions.CultureInvariant);
        int width = sizeMatch.Success ? int.Parse(sizeMatch.Groups[1].Value) : 0;
        int height = sizeMatch.Success ? int.Parse(sizeMatch.Groups[2].Value) : 0;
        return JsonSerializer.Serialize(new
        {
            format = new { duration = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            streams = width > 0 ? new[] { new { duration = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), width, height } } : Array.Empty<object>()
        });
    }

    private async Task<string> ReadInputMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(_ffmpeg)) throw new FileNotFoundException("缺少媒体工具：ffmpeg.exe", _ffmpeg);
        using var process = Process.Start(new ProcessStartInfo(_ffmpeg, $"-hide_banner -i {Quote(path)}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("无法启动媒体工具。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        if (!error.Contains("Duration:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "无法读取媒体信息。" : error);
        return error;
    }

    public async Task<string> CreateThumbnail(string video, string output, double preferredSeconds, CancellationToken cancellationToken = default)
    {
        double middle = Math.Max(0, preferredSeconds);
        // Reject black intros/fades by trying several points around the middle.
        double[] positions = { middle, middle * 0.5, middle * 1.5, middle * 0.2, 1, 0.5, 0 };
        Exception? lastError = null;
        string bestCandidate = output + ".best.jpg";
        double bestScore = double.MinValue;
        TryDelete(bestCandidate);
        foreach (double position in positions.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(output);
            string start = position.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                await RunAsync(_ffmpeg,
                    $"-y -fflags +genpts+discardcorrupt -err_detect ignore_err -ss {start} -i {Quote(video)} -map 0:v:0 -frames:v 1 -vf scale=320:-2:flags=lanczos -q:v 5 -an -sn {Quote(output)}",
                    cancellationToken: cancellationToken);
                if (File.Exists(output) && new FileInfo(output).Length >= 128)
                {
                    var quality = MeasureFrame(output);
                    if (quality.Score > bestScore)
                    {
                        File.Copy(output, bestCandidate, true);
                        bestScore = quality.Score;
                    }
                    if (!quality.IsNearBlack)
                    {
                        TryDelete(bestCandidate);
                        return output;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lastError = ex; }
        }
        // If every sampled frame is dark, use the brightest candidate rather
        // than the last timestamp (which is often a black opening frame).
        if (File.Exists(bestCandidate))
        {
            File.Copy(bestCandidate, output, true);
            TryDelete(bestCandidate);
            return output;
        }
        TryDelete(output);
        TryDelete(bestCandidate);
        throw lastError ?? new InvalidOperationException("无法从视频中提取缩略图。");
    }

    public static bool IsLowQualityThumbnail(string path) => MeasureFrame(path).IsNearBlack;

    private static (bool IsNearBlack, double Score) MeasureFrame(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];
            double scale = Math.Min(1, Math.Min(64d / source.PixelWidth, 36d / source.PixelHeight));
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            long brightness = 0;
            int darkPixels = 0;
            int count = converted.PixelWidth * converted.PixelHeight;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int value = (pixels[i] * 11 + pixels[i + 1] * 59 + pixels[i + 2] * 30) / 100;
                brightness += value;
                if (value < 18) darkPixels++;
            }
            if (count == 0) return (true, double.MinValue);
            double average = brightness / (double)count;
            double darkRatio = darkPixels / (double)count;
            // Penalise images dominated by black pixels. This also catches
            // letterboxed fades that have a small bright logo in the middle.
            double score = average - darkRatio * 45;
            return (average < 28 || darkRatio > 0.86, score);
        }
        catch { return (true, double.MinValue); }
    }

    public async Task<string> ExtractAlbumArt(string audio, string output, CancellationToken cancellationToken = default)
    {
        TryDelete(output);
        await RunAsync(_ffmpeg, $"-y -i {Quote(audio)} -map 0:v:0 -frames:v 1 -vf scale=640:-2:flags=lanczos {Quote(output)}", cancellationToken: cancellationToken);
        if (!File.Exists(output) || new FileInfo(output).Length < 128) throw new InvalidOperationException("MP3 没有可用的内嵌专辑封面。");
        return output;
    }

    public async Task<string> CreatePreview(string video, string output, double startSeconds, double durationSeconds = 2, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        foreach (double position in new[] { Math.Max(0, startSeconds), 0d }.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(output);
            string start = position.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string duration = Math.Max(0.25, durationSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                await RunAsync(_ffmpeg,
                    $"-y -fflags +genpts+discardcorrupt -err_detect ignore_err -ss {start} -i {Quote(video)} -map 0:v:0 -t {duration} -vf scale=320:-2:flags=bilinear -c:v libx264 -preset ultrafast -crf 31 -pix_fmt yuv420p -movflags +faststart -an -sn {Quote(output)}",
                    cancellationToken: cancellationToken);
                if (File.Exists(output) && new FileInfo(output).Length >= 1024) return output;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lastError = ex; }
        }
        TryDelete(output);
        throw lastError ?? new InvalidOperationException("无法生成动态缩略图。");
    }

    private static async Task<string> RunAsync(string executable, string arguments, bool captureOutput = false, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException($"缺少媒体工具：{Path.GetFileName(executable)}", executable);
        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动媒体工具。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        string output = captureOutput ? await process.StandardOutput.ReadToEndAsync(timeout.Token) : "";
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "媒体处理失败。" : error);
        return output;
    }

    private static string ResolveTool(string name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
