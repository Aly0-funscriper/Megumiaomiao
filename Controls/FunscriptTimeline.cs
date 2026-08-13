using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using VideoShelf.Services;

namespace VideoShelf.Controls;

public sealed class FunscriptTimeline : FrameworkElement
{
    private readonly List<FunscriptTrack> _tracks = [];
    private double _durationMs;
    private double _positionMs;
    public bool HasScript => _tracks.Count > 0;

    public void LoadForVideo(string videoPath)
    {
        _tracks.Clear();
        _durationMs = 0;
        foreach (string scriptPath in FunscriptService.GetExistingPaths(videoPath))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scriptPath));
                if (document.RootElement.TryGetProperty("actions", out var actions))
                {
                    var points = new List<ActionPoint>();
                    foreach (var action in actions.EnumerateArray())
                    {
                        if (action.TryGetProperty("at", out var at) && action.TryGetProperty("pos", out var pos))
                            points.Add(new ActionPoint(at.GetDouble(), Math.Clamp(pos.GetDouble(), 0, 100)));
                    }
                    if (points.Count > 0)
                        _tracks.Add(new FunscriptTrack(GetTrackKind(scriptPath), points));
                }
            }
            catch { }
        }
        if (_tracks.Count > 0) _durationMs = _tracks.SelectMany(track => track.Actions).Max(point => point.At);
        InvalidateVisual();
    }

    public void UpdatePlayback(double positionSeconds, double durationSeconds)
    {
        _positionMs = Math.Max(0, positionSeconds * 1000);
        if (durationSeconds > 0) _durationMs = durationSeconds * 1000;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(24, 29, 37)), null, new Rect(0, 0, width, height), 5, 5);
        if (_tracks.Count == 0 || _durationMs <= 0)
        {
            var text = new FormattedText("未找到同名 funscript", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 10,
                new SolidColorBrush(Color.FromRgb(112, 122, 138)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(8, Math.Max(1, (height - text.Height) / 2)));
            return;
        }

        foreach (FunscriptTrack track in _tracks)
        {
            if (track.Actions.Count < 2) continue;
            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                int step = Math.Max(1, track.Actions.Count / Math.Max(1, (int)width * 2));
                bool started = false;
                for (int i = 0; i < track.Actions.Count; i += step)
                {
                    var point = track.Actions[i];
                    var screen = new Point(point.At / _durationMs * width, (100 - point.Pos) / 100 * (height - 4) + 2);
                    if (!started) { context.BeginFigure(screen, false, false); started = true; }
                    else context.LineTo(screen, true, false);

                    if (i > 0)
                    {
                        var previous = track.Actions[Math.Max(0, i - step)];
                        double elapsed = Math.Max(1, point.At - previous.At);
                        double speed = Math.Abs(point.Pos - previous.Pos) / elapsed * 1000;
                        if (speed > 45)
                        {
                            double x = point.At / _durationMs * width;
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(125, 245, 190, 58)), null, new Rect(x, 1, 1.5, height - 2));
                        }
                    }
                }
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(GetTrackColor(track.Kind)), 1.5), line);
        }

        double cursorX = Math.Clamp(_positionMs / _durationMs * width, 0, width);
        dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(cursorX, 0), new Point(cursorX, height));
    }

    private static FunscriptTrackKind GetTrackKind(string path)
    {
        string fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".Lnip.funscript", StringComparison.OrdinalIgnoreCase)) return FunscriptTrackKind.Lnip;
        if (fileName.EndsWith(".Rnip.funscript", StringComparison.OrdinalIgnoreCase)) return FunscriptTrackKind.Rnip;
        return FunscriptTrackKind.Standard;
    }

    private static Color GetTrackColor(FunscriptTrackKind kind) => kind switch
    {
        FunscriptTrackKind.Lnip => Color.FromRgb(242, 193, 78),
        FunscriptTrackKind.Rnip => Color.FromRgb(229, 107, 111),
        _ => Color.FromRgb(91, 181, 211)
    };

    private enum FunscriptTrackKind { Standard, Lnip, Rnip }
    private readonly record struct FunscriptTrack(FunscriptTrackKind Kind, IReadOnlyList<ActionPoint> Actions);
    private readonly record struct ActionPoint(double At, double Pos);
}
