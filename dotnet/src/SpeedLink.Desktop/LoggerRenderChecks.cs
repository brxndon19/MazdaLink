using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    // Accelerated offline workload. Collection is test instrumentation only.
    internal async Task<Dictionary<string, object?>> ExerciseLoggerRendering(int iterations, bool requireStableViews)
    {
        Show(); Navigate(2); UpdateLayout();
        ConfigureLoggerProfile(LoggerDefinition.Known.Single(d => d.MatchesCalibrationId("L3M6EG000")));
        configuringLogger = true;
        foreach (var row in loggerRows) { row.Selected = true; row.Plotted = false; }
        foreach (var row in loggerRows.Take(6)) row.Plotted = true;
        configuringLogger = false;
        var origin = DateTimeOffset.UtcNow;
        long number = 0;
        long rowBytes = 0, chartBytes = 0, layoutBytes = 0;
        void Tick()
        {
            var elapsed = TimeSpan.FromMilliseconds(++number * 100);
            var readings = loggerRows.ToDictionary(r => r.Id,
                r => new LoggerReading(r.Id, 1 + Math.Sin(number * .01), origin + elapsed, "OK"));
            long before = GC.GetAllocatedBytesForCurrentThread();
            ShowLoggerFrame(new(number, origin + elapsed, elapsed, readings, []), null, true, redraw: false);
            long afterRows = GC.GetAllocatedBytesForCurrentThread();
            DrawLoggerChart();
            long afterCharts = GC.GetAllocatedBytesForCurrentThread();
            UpdateLayout();
            rowBytes += afterRows - before; chartBytes += afterCharts - afterRows;
            layoutBytes += GC.GetAllocatedBytesForCurrentThread() - afterCharts;
        }
        for (int i = 0; i < 320; i++) Tick();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var initialViews = LoggerPlots.Children.Cast<UIElement>().ToArray();
        long startLive = GC.GetTotalMemory(true), startAllocated = GC.GetTotalAllocatedBytes(true);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        long startResident = process.WorkingSet64;
        var samples = new List<object>(); var timer = Stopwatch.StartNew();
        rowBytes = chartBytes = layoutBytes = 0;
        for (int i = 1; i <= iterations; i++)
        {
            Tick();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (i % 250 == 0)
            {
                process.Refresh();
                samples.Add(new { iteration = i, residentBytes = process.WorkingSet64, managedLiveBytes = GC.GetTotalMemory(false),
                    allocatedBytes = GC.GetTotalAllocatedBytes(false) - startAllocated });
            }
        }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        long allocated = GC.GetTotalAllocatedBytes(true) - startAllocated;
        long endLive = GC.GetTotalMemory(true); process.Refresh();
        bool stable = initialViews.SequenceEqual(LoggerPlots.Children.Cast<UIElement>());
        if (loggerRows.Any(r => r.Points.Count > 300) || LoggerPlots.Children.Count != 6)
            throw new InvalidOperationException("Live chart history or chart count is unbounded.");
        if (requireStableViews && (!stable || chartBytes / iterations > 160_000 || endLive - startLive > 8 * 1024 * 1024))
            throw new InvalidOperationException($"Live chart allocation regression: stable={stable}, allocated/draw={allocated / iterations}, retained={endLive - startLive}.");
        var firstRow = loggerRows[0];
        int notifications = 0;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, _) => notifications++;
        var unchanged = new LoggerReading(firstRow.Id, 1, origin + TimeSpan.FromHours(1), "OK");
        firstRow.Observe(unchanged, loggerDefinition, null);
        firstRow.PropertyChanged += observer;
        for (int i = 0; i < 100; i++) firstRow.Observe(unchanged, loggerDefinition, null);
        firstRow.PropertyChanged -= observer;
        if (notifications != 0) throw new InvalidOperationException("Unchanged samples repeatedly invalidated grid bindings.");
        var chart = liveCharts[firstRow.Id]; firstRow.Points.Clear();
        foreach (int second in new[] { 2, 3, 11, 12 }) firstRow.Points.Add((origin.AddSeconds(second), 0));
        chart.Update(firstRow, origin, origin.AddSeconds(30), 30, 300);
        if (chart.SegmentCount != 2 || chart.PointCount != 4 || chart.PlotBounds.IsEmpty || !double.IsFinite(chart.PlotBounds.Top))
            throw new InvalidOperationException("Flat values or missing-data chart gaps were drawn incorrectly.");
        firstRow.Points.Clear(); chart.Update(firstRow, origin, origin.AddSeconds(30), 30, 300);
        if (chart.PointCount != 0 || !chart.PlotBounds.IsEmpty) throw new InvalidOperationException("A chart retained obsolete plot data.");
        configuringLogger = true;
        for (int selection = 0; selection < loggerRows.Count; selection++)
        {
            for (int row = 0; row < loggerRows.Count; row++) loggerRows[row].Plotted = (row + selection) % loggerRows.Count < 6;
            DrawLoggerChart();
            if (liveCharts.Count != 6 || LoggerPlots.Children.Count != 6)
                throw new InvalidOperationException("Changing plotted channels retained unused views.");
        }
        configuringLogger = false;
        return new() { ["passed"] = true, ["hardwareAccessed"] = false, ["iterations"] = iterations,
            ["channels"] = loggerRows.Count, ["charts"] = LoggerPlots.Children.Count, ["stableChartViews"] = stable,
            ["allocatedBytesPerUpdate"] = allocated / iterations, ["managedLiveBytesBefore"] = startLive,
            ["rowBytesPerUpdate"] = rowBytes / iterations, ["chartBytesPerUpdate"] = chartBytes / iterations,
            ["layoutBytesPerUpdate"] = layoutBytes / iterations,
            ["managedLiveBytesAfterCollection"] = endLive, ["residentBytesBefore"] = startResident,
            ["residentBytesAfter"] = process.WorkingSet64, ["seconds"] = timer.Elapsed.TotalSeconds, ["samples"] = samples,
            ["unchangedBindingsQuiet"] = true, ["gapsAndFlatValuesVerified"] = true, ["selectionChangesBounded"] = true,
            ["collectionScope"] = "Test instrumentation only; production does not force collection." };
    }
}
