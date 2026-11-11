using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

/// <summary>One retained view per selected live channel; only plot data changes on refresh.</summary>
internal sealed class LiveLoggerChart : StackPanel
{
    private readonly TextBlock title = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock status = new() { Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly LiveLoggerPlot plot = new() { Height = 128, ClipToBounds = true, Margin = new(0, 7, 0, 4) };
    internal int PointCount { get; private set; }
    internal int SegmentCount { get; private set; }
    internal Rect PlotBounds => plot.Bounds;

    public LiveLoggerChart()
    {
        Margin = new(8, 0, 8, 16);
        Children.Add(title); Children.Add(plot); Children.Add(status);
    }

    public void Update(LoggerRow row, DateTimeOffset start, DateTimeOffset end, double seconds, double width)
    {
        title.Text = $"{row.Name} · {row.ValueText} {row.Unit}"; title.ToolTip = row.Channel.Help;
        status.Text = $"{row.Status} · {row.RateText} Hz · shared {seconds:0.#} s time axis";
        double min = double.MaxValue, max = double.MinValue;
        int count = 0;
        foreach (var point in row.Points)
            if (point.Time >= start && point.Time <= end)
            { min = Math.Min(min, point.Value); max = Math.Max(max, point.Value); count++; }
        PointCount = count > 1 ? count : 0; SegmentCount = 0;
        StreamGeometry? geometry = null;
        if (count > 1)
        {
            double scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max)));
            double low = min / scale, high = max / scale;
            if (high - low < 1e-12) { low -= .5; high += .5; }
            geometry = new();
            using var drawing = geometry.Open(); DateTimeOffset? previous = null;
            foreach (var point in row.Points)
            {
                if (point.Time < start || point.Time > end) continue;
                var position = new Point(8 + (point.Time - start).TotalSeconds / seconds * (width - 16),
                    120 - (point.Value / scale - low) / (high - low) * 112);
                if (previous is null || point.Time - previous > LiveLogger.MaximumSampleAge)
                { drawing.BeginFigure(position, isFilled: false, isClosed: false); SegmentCount++; }
                else drawing.LineTo(position, isStroked: true, isSmoothJoin: false);
                previous = point.Time;
            }
        }
        geometry?.Freeze(); plot.Update(geometry);
    }
}

internal sealed class LiveLoggerPlot : FrameworkElement
{
    private static readonly Pen Stroke = new(UiTheme.Amber, 1.8);
    private StreamGeometry? geometry;
    internal Rect Bounds => geometry?.Bounds ?? Rect.Empty;
    internal void Update(StreamGeometry? value) { geometry = value; InvalidateVisual(); }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(UiTheme.Panel, null, new Rect(RenderSize));
        if (geometry is not null) drawing.DrawGeometry(null, Stroke, geometry);
    }
}
