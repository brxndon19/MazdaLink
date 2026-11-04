using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed record LoggerPollingRate(int Target)
{
    public override string ToString() => Target == 0 ? "Max" : Target.ToString(CultureInfo.CurrentCulture);
}

internal sealed class LoggerRow(LoggerChannel channel) : INotifyPropertyChanged
{
    public LoggerChannel Channel { get; } = channel;
    public string Id => Channel.Id;
    public string Name => Channel.Name;
    public string Group => Channel.Group;
    public IReadOnlyList<string> Units => Channel.DisplayUnits;
    private bool selected, plotted, canEdit = true;
    public bool Plotted { get => plotted; set { if (plotted == value) return; plotted = value; Changed(nameof(Plotted)); } }
    private string unit = channel.Unit;
    public bool Selected { get => selected; set { selected = value; Changed(nameof(Selected)); } }
    public bool CanEdit { get => canEdit; set { canEdit = value; Changed(nameof(CanEdit)); } }
    public string Unit { get => unit; set { unit = value; Changed(nameof(Unit)); } }
    public string ValueText { get; private set; } = "—";
    public string Status { get; private set; } = "Not logging";
    public string RateText { get; private set; } = "—";
    public List<(DateTimeOffset Time, double Value)> Points { get; } = [];
    private readonly Queue<DateTimeOffset> times = new();
    private DateTimeOffset? last;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
    public void Reset()
    {
        Points.Clear(); times.Clear(); last = null; SetUnavailable(Selected ? "Waiting" : "Not selected");
    }
    public void SetUnavailable(string state)
    {
        SetDisplay("—", state, "—");
    }
    private void SetDisplay(string value, string state, string rate)
    {
        if (ValueText != value) { ValueText = value; Changed(nameof(ValueText)); }
        if (Status != state) { Status = state; Changed(nameof(Status)); }
        if (RateText != rate) { RateText = rate; Changed(nameof(RateText)); }
    }
    public bool ExpireIfStale(DateTimeOffset now)
    {
        if (last is not DateTimeOffset timestamp || ValueText == "—" || now - timestamp <= LiveLogger.MaximumSampleAge) return false;
        SetUnavailable("Stale · waiting for sample"); return true;
    }
    public void Observe(LoggerReading reading, LoggerDefinition definition, IReadOnlyDictionary<string, double>? parameters, DateTimeOffset? frameTime = null)
    {
        if (reading.Value is not double value || reading.Timestamp is not DateTimeOffset timestamp)
        {
            if (reading.Status == "Waiting" && last is DateTimeOffset previous && frameTime is DateTimeOffset now && ValueText != "—")
            {
                var age = now - previous;
                if (age >= TimeSpan.Zero && age <= LiveLogger.MaximumSampleAge)
                {
                    string state = $"Pending · {age.TotalMilliseconds:0} ms old";
                    if (Status != state) { Status = state; Changed(nameof(Status)); }
                    return;
                }
                SetUnavailable("Stale · waiting for sample"); return;
            }
            SetUnavailable(reading.Status); return;
        }
        double display = definition.ConvertDisplay(Id, value, Unit, parameters);
        if (timestamp != last)
        {
            if (last is not null && timestamp < last) throw new InvalidDataException("Logger time moved backwards.");
            last = timestamp; Points.Add((timestamp, display));
            if (Points.Count > 300) Points.RemoveAt(0);
            times.Enqueue(timestamp);
            while (times.Count > 20) times.Dequeue();
        }
        string rate = times.Count >= 2 && timestamp > times.Peek() ?
            ((times.Count - 1) / (timestamp - times.Peek()).TotalSeconds).ToString("0.0", CultureInfo.CurrentCulture) : "—";
        SetDisplay(display.ToString("0.###", CultureInfo.CurrentCulture), reading.Status, rate);
    }
}

public partial class MainWindow
{
    private LoggerDefinition loggerDefinition = null!;
    private LoggerCalibration? loggerCalibration;
    private readonly ObservableCollection<LoggerRow> loggerRows = [];
    private bool loggingActive, loggerReadReconnectRequired;
    private string? latestRecordingPath;
    private bool openingLatestRecording;
    private long loggerScans;
    private LoggerPreferences loggerPreferences = new();
    private bool loggerSettingsReady, configuringLogger;
    private static readonly string[] EssentialChannels = ["engine_rpm", "absolute_load", "boost_pressure", "actual_lambda", "desired_lambda",
        "fuel_rail_pressure", "desired_fuel_rail_pressure", "injection_pulsewidth", "injector_duty_cycle", "knock_retard", "boost_air_temperature", "relative_throttle_position"];
    private string LoggerFolder => System.IO.Path.Combine(vault, "logs");

    private void InitializeLogger()
    {
        ConfigureLoggerProfile(definitionSnapshot.LoggerFor("L38KEH000")!);
        LoggerGrid.ItemsSource = loggerRows;
        LoggerRateBox.ItemsSource = new[] { 0, 1, 2, 5, 10, 20 }.Select(n => new LoggerPollingRate(n)).ToArray();
        LoggerRateBox.SelectedValuePath = nameof(LoggerPollingRate.Target); LoggerRateBox.SelectedValue = 0;
        var view = CollectionViewSource.GetDefaultView(loggerRows);
        view.Filter = item => item is LoggerRow row && (row.Name.Contains(LoggerSearchBox.Text, StringComparison.CurrentCultureIgnoreCase) || row.Group.Contains(LoggerSearchBox.Text, StringComparison.CurrentCultureIgnoreCase));
        LoadLoggerSettings();
        loggerSettingsReady = true;
        LoggerGrid.SelectedItem = loggerRows.First(row => row.Id == "engine_rpm");
    }
    private void ConfigureLoggerProfile(LoggerDefinition profile)
    {
        if (loggerSettingsReady) RememberLoggerProfile();
        var saved = loggerPreferences.ForProfile(profile.Id).ToDictionary(c => c.Id, StringComparer.Ordinal);
        var previous = loggerRows.ToDictionary(row => row.Id, row => (row.Selected, row.Unit, row.Plotted));
        configuringLogger = true;
        loggerDefinition = profile; loggerCalibration = null; loggerRows.Clear();
        LoggerRomText.Text = "Load the current ECU backup for its MAP calibration.";
        LoggerProfileText.Text = $"{profile.Vehicle} · {string.Join(", ", profile.SoftwareIds)} · {profile.ChannelIds.Count} parameters · ECU testing pending";
        LoggerProfileText.ToolTip = profile.UnavailableChannels.Count == 0 ? "All parameters in the current logging set are available in this profile." :
            "Not offered by this ROM profile: " + string.Join(", ", profile.UnavailableChannels.Select(c => c.Name));
        foreach (var channel in profile.Channels)
        {
            var row = new LoggerRow(channel) { Selected = previous.TryGetValue(channel.Id, out var prior) ? prior.Selected : EssentialChannels.Contains(channel.Id, StringComparer.Ordinal) };
            row.Plotted = previous.TryGetValue(channel.Id, out prior) ? prior.Plotted : new[] { "engine_rpm", "boost_pressure", "actual_lambda" }.Contains(channel.Id);
            if (previous.TryGetValue(channel.Id, out prior) && row.Units.Contains(prior.Unit)) row.Unit = prior.Unit;
            if (saved.TryGetValue(channel.Id, out var choice))
            {
                row.Selected = choice.Selected;
                if (row.Units.Contains(choice.Unit)) row.Unit = choice.Unit;
                if (choice.Plotted is bool plotted) row.Plotted = plotted;
            }
            if (row.Plotted && loggerRows.Count(r => r.Plotted) >= 6) row.Plotted = false;
            row.PropertyChanged += (_, e) =>
            {
                if (configuringLogger) return;
                if (e.PropertyName == nameof(LoggerRow.Selected) && IsLoaded) Controls();
                if (e.PropertyName == nameof(LoggerRow.Plotted))
                {
                    if (loggerRows.Count(r => r.Plotted) > 6) { row.Plotted = false; LoggerHelpText.Text = "Select up to six plots."; return; }
                    DrawLoggerChart();
                }
                if (e.PropertyName == nameof(LoggerRow.Selected)) DrawLoggerChart();
                if (e.PropertyName == nameof(LoggerRow.Unit)) { row.Reset(); DrawLoggerChart(); }
            };
            loggerRows.Add(row);
        }
        LoggerGrid.SelectedItem = loggerRows.First(row => row.Id == "engine_rpm");
        configuringLogger = false;
        DrawLoggerChart();
    }
    private void LoggerIdentityChanged()
    {
        var match = definitionSnapshot.LoggerFor(identity?.Calibration);
        if (match is not null && match.Id != loggerDefinition.Id) ConfigureLoggerProfile(match);
        if (match is null)
        {
            loggerCalibration = null;
            LoggerProfileText.Text = "No logger profile matches this ECU calibration. ROM backup reading is available.";
            LoggerRomText.Text = "A matching supported ROM is needed for logging.";
        }
    }

    private void LoggerControls(bool idle)
    {
        if (LoggerStartButton is null || loggerDefinition is null) return;
        LoggerStartButton.IsEnabled = idle && transport is not null && !transport.ReadAttempted && loggerCalibration is not null &&
            definitionSnapshot.LoggerFor(identity?.Calibration) is not null && loggerDefinition.MatchesCalibrationId(identity?.Calibration) && loggerRows.Any(row => row.Selected);
        LoggerStopButton.IsEnabled = loggingActive && !closing;
        ViewRecordingButton.IsEnabled = idle && !openingLatestRecording && latestRecordingPath is not null;
        ViewRecordingButton.ToolTip = loggingActive ? "Stop logging to open this recording."
            : latestRecordingPath is null ? "Record a log to view it here. Older logs can be opened on Recordings."
            : "Open the recording from the latest recorded session.";
        LoggerRomButton.IsEnabled = idle; LoggerRateBox.IsEnabled = idle; StoichBox.IsEnabled = idle; RecordCsvBox.IsEnabled = idle;
        LoggerEssentialsButton.IsEnabled = idle; LoggerAllButton.IsEnabled = idle; LoggerNoneButton.IsEnabled = idle;
        foreach (var row in loggerRows) row.CanEdit = idle;
        string reason = transport?.ReadAttempted == true ? "After a ROM read, disconnect and reconnect on Connection before logging."
            : transport is null ? "Connect and identify the ECU, then load its matching ROM."
            : loggerCalibration is null ? "Load the current ECU ROM before logging."
            : !loggerDefinition.MatchesCalibrationId(identity?.Calibration) ? "Load a ROM matching the connected ECU calibration."
            : !loggerRows.Any(row => row.Selected) ? "Select at least one parameter to log." : "Start a new logging session.";
        LoggerStartButton.ToolTip = reason;
        LoggerConnectionHint.Text = reason;
        LoggerConnectionHint.Visibility = LoggerConnectionButton.Visibility = transport?.ReadAttempted == true ? Visibility.Visible : Visibility.Collapsed;
        LoggerConnectionButton.IsEnabled = idle;
    }
    private void LoggerConnection_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void BindLoggerRom(string path)
    {
        if (new FileInfo(path).Length != RomInspector.RomSize) throw new InvalidDataException("Load a full 1 MiB ECU image.");
        var binding = definitionSnapshot.BindLoggerRom(File.ReadAllBytes(path));
        if (identity is not null && (definitionSnapshot.LoggerFor(identity.Calibration) is null || !binding.Definition.MatchesCalibrationId(identity.Calibration)))
            throw new InvalidDataException($"The selected ROM ({string.Join(", ", binding.Definition.SoftwareIds)}) does not match the connected ECU calibration ({identity.Calibration ?? "not reported"}).");
        ConfigureLoggerProfile(binding.Definition); loggerCalibration = binding.Calibration;
        LoggerRomText.Text = $"{string.Join(", ", binding.Definition.SoftwareIds)} · {System.IO.Path.GetFileName(path)} · MAP calibration loaded";
        Log("Logger ROM: " + binding.Calibration.SourceRomSha256);
        Controls();
    }
    private void LoggerRom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select the current ECU backup · logger profile is detected automatically", Filter = "ROM images|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(this) == true) TryFile(() => BindLoggerRom(dialog.FileName));
    }
    private IReadOnlyDictionary<string, double>? LoggerDisplayParameters()
    {
        if (string.IsNullOrWhiteSpace(StoichBox.Text)) return null;
        if (!double.TryParse(StoichBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double stoich) || !double.IsFinite(stoich) || stoich is < 1 or > 30)
            throw new InvalidDataException("Enter a fuel stoichiometric AFR between 1 and 30, or leave it blank for lambda.");
        return new Dictionary<string, double> { ["stoichiometric_afr"] = stoich };
    }
    private async void LoggerStart_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || exportingReport || loggingActive || transport is null || identity is null || loggerCalibration is null) return;
        var link = transport; var ecu = identity; var calibration = loggerCalibration;
        var selected = loggerRows.Where(row => row.Selected).ToArray();
        var units = selected.ToDictionary(row => row.Id, row => row.Unit);
        int target = (int)LoggerRateBox.SelectedValue;
        bool recording = RecordCsvBox.IsChecked == true;
        IReadOnlyDictionary<string, double>? parameters;
        LiveLogger logger;
        try
        {
            parameters = LoggerDisplayParameters();
            foreach (var row in selected) loggerDefinition.ConvertDisplay(row.Id, 0, row.Unit, parameters);
            logger = new LiveLogger(loggerDefinition, calibration, link, ecu, selected.Select(row => row.Id));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or TransportException)
        {
            LoggerHelpText.Text = error.Message; StatusText.Text = error.Message; Log(error.Message); return;
        }
        loggingActive = true;
        loggerReadReconnectRequired = true;
        await Run(async () =>
        {
            bool stoppedCleanly = false;
            bool recordingCreated = false;
            var display = new LoggerDisplayBuffer();
            void RenderPending()
            {
                var batch = display.Drain();
                foreach (var update in batch) ShowLoggerFrame(update.Frame, parameters, update.ScanComplete, redraw: false);
                bool expired = false;
                foreach (var row in selected) expired |= row.ExpireIfStale(logger.CurrentTime);
                if (batch.Count > 0 || expired) DrawLoggerChart();
            }
            var displayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            displayTimer.Tick += (_, _) => RenderPending();
            try
            {
                diagnosticSession?.Record("logger.started", new { profile = loggerDefinition.SoftwareIds, loggerDefinition.DefinitionVersion, channels = units, targetScansPerSecond = target, recording });
                SaveLoggerSettings();
                foreach (var row in loggerRows) row.Reset();
                loggerScans = 0;
                LoggerFileText.Text = recording ? "Starting a new recording…" : "Live view · CSV recording off";
                StatusText.Text = "Logging · stock application identifiers";
                displayTimer.Start();
                await Task.Run(() =>
                {
                    stop!.Token.ThrowIfCancellationRequested();
                    var current = ReadWorkflow.Identify(link);
                    if (!current.MatchesVehicle(ecu))
                        throw new TransportException("ECU identity changed. Reconnect and identify before logging.");
                    stop.Token.ThrowIfCancellationRequested();
                    LoggerRecording? file = null;
                    long recordedScan = 0;
                    string finish = "Stopped";
                    try
                    {
                        if (recording)
                        {
                            file = new(LoggerFolder, loggerDefinition, calibration, ecu, selected.Select(row => row.Id), units, parameters, Version, target);
                            recordingCreated = true;
                            Dispatcher.Invoke(() =>
                            {
                                LoggerFileText.Text = file.Folder;
                                latestRecordingPath = System.IO.Path.Combine(file.Folder, "log.csv");
                            });
                        }
                        while (!stop!.IsCancellationRequested)
                        {
                            var scanTimer = Stopwatch.StartNew();
                            var frame = logger.ReadFrame(stop.Token, partial => display.Publish(partial, false));
                            recordedScan = frame.Scan; file?.Append(frame);
                            display.Publish(frame, true);
                            if (logger.ActiveQueryCount == 0) throw new TransportException("The ECU rejected every selected query. Logging stopped; inspect channel statuses and reconnect.");
                            int delay = target > 0 ? (int)Math.Max(0, 1000.0 / target - scanTimer.Elapsed.TotalMilliseconds) : 0;
                            if (delay > 0) stop.Token.WaitHandle.WaitOne(delay);
                        }
                    }
                    catch (OperationCanceledException) when (stop!.IsCancellationRequested)
                    {
                        if (file is not null && logger.LastFrame is { } partial && partial.Scan > recordedScan)
                        { recordedScan = partial.Scan; file.Append(partial); }
                    }
                    catch (Exception error)
                    {
                        finish = "Stopped after error: " + error.Message;
                        if (file is not null && logger.LastFrame is { } partial && partial.Scan > recordedScan)
                        {
                            recordedScan = partial.Scan; file.Append(partial);
                            display.Publish(partial, true);
                        }
                        throw;
                    }
                    finally
                    {
                        try { file?.Complete(finish); }
                        finally { file?.Dispose(); }
                    }
                });
                stoppedCleanly = true;
            }
            catch (OperationCanceledException) when (stop!.IsCancellationRequested)
            {
                // Also allow Stop before the first poll or recording has been created.
                stoppedCleanly = true;
            }
            finally
            {
                loggingActive = false;
                displayTimer.Stop();
                try { RenderPending(); }
                catch { stoppedCleanly = false; await Disconnect(); throw; }
                // Cancellation is observed between bounded exchanges; a clean Stop leaves no reply in flight.
                // Preserve that connection for another log, but never reuse an uncertain failed exchange.
                if (!stoppedCleanly || closing) await Disconnect();
                diagnosticSession?.Record("logger.stopped", new { stoppedCleanly, connectionRetained = transport is not null, completedScans = loggerScans, recordingCreated, displayUpdatesDropped = display.Dropped });
                foreach (var row in selected) row.SetUnavailable(row.Status.StartsWith("Unavailable", StringComparison.Ordinal) ? "Stopped · " + row.Status :
                    transport is not null ? "Stopped" : "Stopped · reconnect to log");
                DrawLoggerChart();
            }
            if (recording && !recordingCreated) LoggerFileText.Text = "Stopped before a recording was created";
            StatusText.Text = recordingCreated ? "Logging stopped · recording saved · ready to start again" : "Logging stopped · ready to start again";
        });
    }
    private void ShowLoggerFrame(LoggerFrame frame, IReadOnlyDictionary<string, double>? parameters, bool scanComplete, bool redraw = true)
    {
        foreach (var row in loggerRows)
            if (frame.Readings.TryGetValue(row.Id, out var reading)) row.Observe(reading, loggerDefinition, parameters, frame.Timestamp);
        if (scanComplete)
        {
            loggerScans = frame.Scan;
            double elapsed = frame.Elapsed.TotalSeconds;
            LoggerRateText.Text = $"{frame.Scan:N0} scans · {(elapsed > 0 ? frame.Scan / elapsed : 0):0.0} scans/s overall · {frame.Exchanges.Count} requests in last scan · per-channel Hz shown above";
        }
        if (redraw) DrawLoggerChart();
    }
    private void LoggerEssentials_Click(object sender, RoutedEventArgs e) { foreach (var row in loggerRows) row.Selected = EssentialChannels.Contains(row.Id, StringComparer.Ordinal); }
    private void LoggerAll_Click(object sender, RoutedEventArgs e) { foreach (var row in loggerRows) row.Selected = true; }
    private void LoggerNone_Click(object sender, RoutedEventArgs e) { foreach (var row in loggerRows) row.Selected = false; }
    private void LoggerSearch_Changed(object sender, TextChangedEventArgs e) { if (loggerRows.Count > 0) CollectionViewSource.GetDefaultView(loggerRows).Refresh(); }
    private void LoggerSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LoggerGrid.SelectedItem is LoggerRow row) LoggerHelpText.Text = row.Channel.Help;
        DrawLoggerChart();
    }
    private bool drawingLogger;
    private readonly Dictionary<string, LiveLoggerChart> liveCharts = new(StringComparer.Ordinal);
    private TextBlock? emptyLoggerChart;
    private void LoggerChart_SizeChanged(object sender, SizeChangedEventArgs e) { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) DrawLoggerChart(); }
    private void DrawLoggerChart()
    {
        if (LoggerPlots is null || drawingLogger) return;
        drawingLogger = true;
        try
        {
            var charts = loggerRows.Where(r => r.Plotted && r.Selected).Take(6).ToArray();
            foreach (string id in liveCharts.Keys.Where(id => !charts.Any(r => r.Id == id)).ToArray()) liveCharts.Remove(id);
            if (charts.Length == 0)
            {
                emptyLoggerChart ??= new TextBlock { Text = "Select Log and Plot for up to six parameters.", Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new(12) };
                if (LoggerPlots.Children.Count != 1 || !ReferenceEquals(LoggerPlots.Children[0], emptyLoggerChart))
                { LoggerPlots.Children.Clear(); LoggerPlots.Children.Add(emptyLoggerChart); }
                return;
            }
            foreach (var row in charts) if (!liveCharts.ContainsKey(row.Id)) liveCharts.Add(row.Id, new());
            if (LoggerPlots.Children.Count != charts.Length || charts.Where((row, index) => !ReferenceEquals(LoggerPlots.Children[index], liveCharts[row.Id])).Any())
            { LoggerPlots.Children.Clear(); foreach (var row in charts) LoggerPlots.Children.Add(liveCharts[row.Id]); }
            DateTimeOffset? first = null, last = null;
            foreach (var row in charts)
                if (row.Points.Count > 0)
                {
                    if (first is null || row.Points[0].Time < first) first = row.Points[0].Time;
                    if (last is null || row.Points[^1].Time > last) last = row.Points[^1].Time;
                }
            var end = last ?? DateTimeOffset.UtcNow;
            double seconds = first is null ? 30 : Math.Clamp((end - first.Value).TotalSeconds, 1, 30);
            var start = end.AddSeconds(-seconds); double width = Math.Max(100, LoggerPlots.ActualWidth - 24);
            foreach (var row in charts) liveCharts[row.Id].Update(row, start, end, seconds, width);
        }
        finally { drawingLogger = false; }
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        TryFile(() => { Directory.CreateDirectory(LoggerFolder); Process.Start(new ProcessStartInfo(LoggerFolder) { UseShellExecute = true }); });
    }
    private async void ViewRecording_Click(object sender, RoutedEventArgs e)
    {
        if (loggingActive || openingLatestRecording || latestRecordingPath is not string path) return;
        openingLatestRecording = true; Controls(); Navigate(3);
        try
        {
            await RecordingsPane.OpenAsync(path);
            StatusText.Text = "Recording opened · " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        { StatusText.Text = "Recording could not be opened: " + error.Message; Log(StatusText.Text); }
        catch (OperationCanceledException) { }
        finally { openingLatestRecording = false; Controls(); }
    }
    private void RememberLoggerProfile()
    {
        if (loggerDefinition is null) return;
        loggerPreferences.Profiles[loggerDefinition.Id] = loggerRows.Select(row => new LoggerChannelPreference(row.Id, row.Selected, row.Unit, row.Plotted)).ToArray();
    }
    private void SaveLoggerSettings()
    {
        if (!loggerSettingsReady) return;
        RememberLoggerProfile();
        loggerPreferences.Target = LoggerRateBox.SelectedValue is int target ? target : 0;
        loggerPreferences.Stoich = StoichBox.Text; loggerPreferences.Record = RecordCsvBox.IsChecked == true;
        loggerPreferences.ActiveCalibration = loggerDefinition.SoftwareIds.First();
        try { loggerPreferences.Save(System.IO.Path.Combine(vault, "logger-settings.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Log("Logger settings could not be saved: " + error.Message); }
    }
    private void LoadLoggerSettings()
    {
        string path = System.IO.Path.Combine(vault, "logger-settings.json");
        if (!File.Exists(path)) return;
        bool ready = loggerSettingsReady; loggerSettingsReady = false;
        try
        {
            loggerPreferences = LoggerPreferences.Load(path);
            LoggerRateBox.SelectedValue = loggerPreferences.Target;
            StoichBox.Text = loggerPreferences.Stoich; RecordCsvBox.IsChecked = loggerPreferences.Record;
            var priorProfile = loggerDefinition;
            var priorCalibration = loggerCalibration;
            string priorRomText = LoggerRomText.Text;
            ConfigureLoggerProfile(definitionSnapshot.LoggerFor(loggerPreferences.ActiveCalibration) ?? loggerDefinition);
            if (loggerDefinition.Id == priorProfile.Id)
            { loggerCalibration = priorCalibration; LoggerRomText.Text = priorRomText; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Log("Logger settings could not be loaded: " + error.Message); }
        finally { loggerSettingsReady = ready; }
    }
    private void VerifyLoggerSmoke(string[] args)
    {
        if (loggerRows.Count != 38 || LoggerDefinition.Known.Count != RomInspector.KnownProfiles.Count || LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled || loggerCalibration is not null)
            throw new InvalidOperationException("Unexpected logger startup state.");
        if (!Resource("SpeedLink.LoggerHelp.md").Contains("Start logging", StringComparison.Ordinal)) throw new InvalidDataException("Logger help missing.");
        Pages.SelectedIndex = 2; LoggerSearchBox.Text = "fuel";
        if (CollectionViewSource.GetDefaultView(loggerRows).IsEmpty) throw new InvalidOperationException("Logger search failed.");
        LoggerSearchBox.Text = "";
        int rom = Array.IndexOf(args, "--logger-rom");
        if (rom >= 0 && rom + 1 < args.Length) BindLoggerRom(args[rom + 1]);
        UpdateLayout(); DrawLoggerChart();
        Pages.SelectedIndex = args.Contains("--logger-page", StringComparer.Ordinal) ? 2 : 0;
    }
}
