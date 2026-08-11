using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace VideoShelf.Controls;

public sealed class FunscriptTimeline : FrameworkElement
{
    private readonly List<ActionPoint> _actions = [];
    private double _durationMs;
    private double _positionMs;
    public bool HasScript => _actions.Count > 0;

    public void LoadForVideo(string videoPath)
    {
        _actions.Clear();
        _durationMs = 0;
        string scriptPath = Path.ChangeExtension(videoPath, ".funscript");
        try
        {
            if (File.Exists(scriptPath))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scriptPath));
                if (document.RootElement.TryGetProperty("actions", out var actions))
                {
                    foreach (var action in actions.EnumerateArray())
                    {
                        if (action.TryGetProperty("at", out var at) && action.TryGetProperty("pos", out var pos))
                            _actions.Add(new ActionPoint(at.GetDouble(), Math.Clamp(pos.GetDouble(), 0, 100)));
                    }
                }
            }
        }
        catch { _actions.Clear(); }
        if (_actions.Count > 0) _durationMs = _actions[^1].At;
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
        if (_actions.Count < 2 || _durationMs <= 0)
        {
            var text = new FormattedText("未找到同名 funscript", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 10,
                new SolidColorBrush(Color.FromRgb(112, 122, 138)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(8, Math.Max(1, (height - text.Height) / 2)));
            return;
        }

        var line = new StreamGeometry();
        using (var context = line.Open())
        {
            int step = Math.Max(1, _actions.Count / Math.Max(1, (int)width * 2));
            bool started = false;
            for (int i = 0; i < _actions.Count; i += step)
            {
                var point = _actions[i];
                var screen = new Point(point.At / _durationMs * width, (100 - point.Pos) / 100 * (height - 4) + 2);
                if (!started) { context.BeginFigure(screen, false, false); started = true; }
                else context.LineTo(screen, true, false);

                if (i > 0)
                {
                    var previous = _actions[Math.Max(0, i - step)];
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
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(91, 181, 211)), 1.5), line);

        double cursorX = Math.Clamp(_positionMs / _durationMs * width, 0, width);
        dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(cursorX, 0), new Point(cursorX, height));
    }

    private readonly record struct ActionPoint(double At, double Pos);
}
