using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed class ReplayChannel(RecordedChannel channel) : INotifyPropertyChanged
{
    public RecordedChannel Data { get; } = channel;
    public string Name => Data.Name;
    public string Unit => Data.Unit;
    private bool selected;
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class LogViewerWorkspace : UserControl
{
    private RecordedLog? log;
    private readonly ObservableCollection<ReplayChannel> rows = [];
    private CancellationTokenSource? loading;
    private bool drawing;
    private static readonly string[] PreferredChannels = ["engine_rpm", "boost_pressure", "actual_lambda", "fuel_rail_pressure", "knock_retard", "relative_throttle_position"];
    internal int PlottedChannels => rows.Count(r => r.Selected);
    internal RecordedLog? Recording => log;
    internal string? OpenedPath => FileText.ToolTip as string;
    internal void RequestOpen() => Open_Click(this, new());
    public LogViewerWorkspace()
    {
        UiTheme.Ensure(); InitializeComponent(); Channels.ItemsSource = rows;

    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open a SpeedLink recording", Filter = "SpeedLink CSV recordings|*.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await OpenAsync(dialog.FileName); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        { MessageBox.Show(Window.GetWindow(this), error.Message, "Recording could not be opened", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (OperationCanceledException) { }
    }
    internal async Task OpenAsync(string path)
    {
        loading?.Cancel();
        var operation = new CancellationTokenSource(); loading = operation; var token = operation.Token;
        OpenButton.IsEnabled = false; Summary.Text = "Opening recording…";
        try
        {
            var loaded = await Task.Run(() => RecordedLog.Load(path, token), token);
            token.ThrowIfCancellationRequested(); log = loaded; rows.Clear();
            foreach (var channel in log.Channels)
            {
                var row = new ReplayChannel(channel) { Selected = PreferredChannels.Contains(channel.Id, StringComparer.Ordinal) };
                row.PropertyChanged += (_, _) =>
                {
                    if (rows.Count(r => r.Selected) > 6) { row.Selected = false; return; }
                    Draw();
                };
                rows.Add(row);
            }
            if (!rows.Any(r => r.Selected)) rows[0].Selected = true;
            FileText.Text = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + " · " + System.IO.Path.GetFileName(path);
            FileText.ToolTip = System.IO.Path.GetFullPath(path);
            TimeCursor.Maximum = Math.Max(.001, log.DurationSeconds); TimeCursor.Value = 0; Zoom.Value = 1;
            Warnings.Text = string.Join(" ", log.Warnings); Draw();
        }
        catch
        {
            if (ReferenceEquals(loading, operation))
            {
                if (log is not null) Draw();
                else Summary.Text = token.IsCancellationRequested ? "Opening cancelled. Choose a recording." : "Recording could not be opened. Choose another file.";
            }
            throw;
        }
        finally
        {
            if (ReferenceEquals(loading, operation)) { loading = null; OpenButton.IsEnabled = true; }
            operation.Dispose();
        }
    }
    internal void CancelLoading() => loading?.Cancel();
    private void Position_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Draw();
    private void Plots_SizeChanged(object sender, SizeChangedEventArgs e) { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Draw(); }
    private void Draw()
    {
        if (log is null || drawing || Plots is null) return;
        drawing = true;
        try
        {
            Plots.Children.Clear(); double duration = Math.Max(.001, log.DurationSeconds);
            double span = Math.Max(Math.Min(.1, duration), duration / Zoom.Value);
            double start = Math.Clamp(TimeCursor.Value - span / 2, 0, Math.Max(0, duration - span)), end = start + span;
            Summary.Text = $"{log.Rows:N0} scans · {log.Channels.Count} channels · {duration:0.###} s · cursor {TimeCursor.Value:0.###} s · view {start:0.###}–{end:0.###} s";
            foreach (var row in rows.Where(r => r.Selected))
            {
                var channel = row.Data; var sample = channel.At(TimeCursor.Value);
                string value = sample?.Value is double v ? v.ToString("0.###", CultureInfo.CurrentCulture) : "—";
                string state = sample is null ? "No observation yet" : sample.Status;
                string age = sample?.SampleSeconds is double a ? $" · sample age {(TimeCursor.Value - a) * 1000:0.#} ms" : "";
                var panel = new StackPanel { Margin = new(0, 0, 0, 12) };
                panel.Children.Add(new TextBlock { Text = $"{channel.Name} · {value} {channel.Unit} · {state}{age}", TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold });
                var canvas = new Canvas { Height = 105, Background = UiTheme.Panel, ClipToBounds = true, Margin = new(0, 6, 0, 3) };
                double width = Math.Max(100, Plots.ActualWidth - 18), height = 89;
                var visible = channel.Samples.Where(p => p.SampleSeconds >= start && p.SampleSeconds <= end && p.Value.HasValue).ToArray();
                double min = visible.Length == 0 ? 0 : visible.Min(p => p.Value!.Value), max = visible.Length == 0 ? 1 : visible.Max(p => p.Value!.Value);
                if (max - min < .001) { min -= .5; max += .5; }
                double scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max)));
                double low = min / scale, high = max / scale;
                if (high - low < 1e-12) { low -= .5; high += .5; }
                double X(double time) => 8 + (time - start) / span * (width - 16);
                // Keep each screen column's first/last points and extrema; never discard a narrow spike.
                foreach (var segment in channel.Plot(start, end, Math.Min(10000, (int)width)))
                {
                    var line = new Polyline { Stroke = UiTheme.Amber, StrokeThickness = 1.7 };
                    foreach (var point in segment) line.Points.Add(new(X(point.SampleSeconds!.Value), 8 + height - (point.Value!.Value / scale - low) / (high - low) * height));
                    canvas.Children.Add(line);
                }
                var cursor = new Line { X1 = X(TimeCursor.Value), X2 = X(TimeCursor.Value), Y1 = 0, Y2 = 105, Stroke = UiTheme.Text, StrokeThickness = 1, StrokeDashArray = [3, 3] };
                canvas.Children.Add(cursor);
                canvas.MouseLeftButtonDown += (_, e) => TimeCursor.Value = Math.Clamp(start + (e.GetPosition(canvas).X - 8) / (width - 16) * span, 0, duration);
                panel.Children.Add(canvas);
                string range = visible.Length == 0 ? "No valid samples in view" : $"{min:0.###}–{max:0.###} {channel.Unit}";
                string maximumAge = channel.MaximumAgeSeconds is double maximum ? $"{maximum * 1000:0.#} ms" : "—";
                panel.Children.Add(new TextBlock { Text = $"{range} · {channel.MeasuredHz:0.##} Hz recorded · {channel.Samples.Count - channel.AvailableCount:N0} unavailable · max recorded age {maximumAge}", Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap });
                Plots.Children.Add(panel);
            }
        }
        finally { drawing = false; }
    }
    internal void SetView(double seconds, double zoom) { TimeCursor.Value = seconds; Zoom.Value = zoom; Draw(); }
    internal async Task<Dictionary<string, object?>> ExerciseOffline(string path, string screenshot)
    {
        await OpenAsync(path);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        SetView(log!.DurationSeconds * .55, 2); UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Draw(); UpdateLayout();
        int expectedDefaults = Math.Max(1, log.Channels.Count(c => PreferredChannels.Contains(c.Id, StringComparer.Ordinal)));
        if (PlottedChannels != expectedDefaults || Plots.Children.Count != PlottedChannels) throw new InvalidOperationException("Replay channel selection or plots failed.");
        foreach (var row in rows) row.Selected = true;
        if (PlottedChannels != Math.Min(6, log.Channels.Count)) throw new InvalidOperationException("Replay selection failed its plot limit.");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (Plots.Children.OfType<StackPanel>().Any(p => p.ActualHeight < 100 || p.ActualWidth < 100)) throw new InvalidOperationException("Replay plots were not laid out.");
        void Capture(string file)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = File.Create(file); encoder.Save(output);
        }
        Capture(screenshot);
        var host = Window.GetWindow(this)!; host.Width = host.MinWidth; host.Height = host.MinHeight; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Draw(); UpdateLayout(); Capture(System.IO.Path.ChangeExtension(screenshot, ".minimum.png"));
        var report = new Dictionary<string, object?> { ["passed"] = true, ["channels"] = log.Channels.Count, ["rows"] = log.Rows,
            ["plottedChannels"] = PlottedChannels, ["cursorSeconds"] = TimeCursor.Value, ["zoom"] = Zoom.Value,
            ["warnings"] = log.Warnings, ["hardwareAccessed"] = false, ["minimumLayout"] = new[] { ActualWidth, ActualHeight }, ["screenshot"] = screenshot };
        return report;
    }
}
