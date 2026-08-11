using System;
using System.IO;
using System.Text.Json;
using VideoShelf.Models;


namespace VideoShelf.Services
{

    public class VideoProcessor
    {


        private readonly FFmpegService ffmpeg =
            new FFmpegService();



        public async Task<VideoInfo> Process(
            VideoInfo info,
            bool generatePreview = false,
            CancellationToken cancellationToken = default)
        {


            string json =
                await ffmpeg.GetVideoInfo(
                    info.FilePath, cancellationToken);



            using var doc =
                JsonDocument.Parse(json);



            var format =
                doc.RootElement
                .GetProperty("format");



            if (format.TryGetProperty("duration", out var duration) && TryReadSeconds(duration, out double formatSeconds))
                info.Duration = TimeSpan.FromSeconds(formatSeconds);





            foreach (var stream in
                doc.RootElement
                .GetProperty("streams")
                .EnumerateArray())
            {

                if (info.Duration <= TimeSpan.Zero && stream.TryGetProperty("duration", out var streamDuration)
                    && TryReadSeconds(streamDuration, out double streamSeconds))
                    info.Duration = TimeSpan.FromSeconds(streamSeconds);

                if (stream.TryGetProperty(
                    "width",
                    out var width))
                {

                    info.Width =
                        width.GetInt32();


                    if (stream.TryGetProperty("height", out var height))
                        info.Height = height.GetInt32();


                    break;

                }
            }





            string thumbFolder = StoragePaths.Thumbnails;



            Directory.CreateDirectory(
                thumbFolder);




            string thumb =
                Path.Combine(
                    thumbFolder,
                    Guid.NewGuid()
                    + ".jpg");



            try
            {
                if (info.IsAudio) await ffmpeg.ExtractAlbumArt(info.FilePath, thumb, cancellationToken);
                else await ffmpeg.CreateThumbnail(info.FilePath, thumb,
                        info.Duration.TotalSeconds > 0 ? Math.Clamp(info.Duration.TotalSeconds * 0.5, 0, Math.Max(0, info.Duration.TotalSeconds - 0.1)) : 1,
                        cancellationToken);
            }
            catch when (info.IsAudio)
            {
                string? fallback = Directory.EnumerateFiles(Path.GetDirectoryName(info.FilePath)!, "*.*")
                    .Where(path => new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
                if (fallback == null) throw;
                File.Copy(fallback, thumb, true);
            }



            info.ThumbnailPath =
                thumb;
            if (info.IsAudio)
            {
                string lrc = Path.ChangeExtension(info.FilePath, ".lrc");
                info.LrcPath = File.Exists(lrc) ? lrc : "";
            }







            if (generatePreview && !info.IsAudio)
                await CreatePreviewOnly(info, cancellationToken);

            info.IsLoading =
                false;
            info.ThumbnailFailed = false;
            info.ThumbnailQualityChecked = true;
            info.ThumbnailError = "";

            return info;

        }

        public async Task<VideoInfo> CreatePreviewOnly(VideoInfo info, CancellationToken cancellationToken = default)
        {
            string previewFolder = StoragePaths.Previews;



            Directory.CreateDirectory(
                previewFolder);




            string preview =
                Path.Combine(
                    previewFolder,
                    Guid.NewGuid()
                    + ".mp4");



            double totalSeconds = info.Duration.TotalSeconds;
            double previewDuration = totalSeconds > 0 ? Math.Min(2, Math.Max(0.25, totalSeconds)) : 2;
            double previewStart = totalSeconds > 0
                ? Math.Clamp(totalSeconds * 0.5, 0, Math.Max(0, totalSeconds - previewDuration))
                : 1;
            await ffmpeg.CreatePreview(
                info.FilePath,
                preview,
                previewStart,
                previewDuration,
                cancellationToken);



            info.PreviewPath =
                preview;
            return info;
        }

        private static bool TryReadSeconds(JsonElement element, out double seconds)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out seconds))
                return double.IsFinite(seconds) && seconds >= 0;
            return double.TryParse(element.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out seconds)
                && double.IsFinite(seconds) && seconds >= 0;
        }


    }

}
