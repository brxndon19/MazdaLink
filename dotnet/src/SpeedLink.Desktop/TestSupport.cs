using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpeedLink.Core;
[assembly: InternalsVisibleTo("SpeedLink.UiHarness")]
namespace SpeedLink.Desktop;

internal sealed class LoggerOnlyTestTransport(IReadTransport inner, LoggerDefinition profile) : IReadTransport
{
    private readonly HashSet<string> allowed = profile.Channels.Where(c => c.Query is not null)
        .Select(c => Convert.ToHexString(profile.RequestFor(c.Id))).Append("0902").Append("0904").ToHashSet(StringComparer.Ordinal);
    public List<string> Requests { get; } = [];
    public bool Disposed { get; private set; }
    public IDiagnosticSink? Diagnostics => inner.Diagnostics;
    public string Mode => inner.Mode;
    public string Adapter => inner.Adapter;
    public int ReadChunkSize => inner.ReadChunkSize;
    public bool ReadAttempted { get => inner.ReadAttempted; set => inner.ReadAttempted = value; }
    public byte[] Exchange(byte[] request)
        => ExchangeCore(request, () => inner.Exchange(request));
    public byte[] ExchangeLogger(byte[] request, int expectedResponseLength)
        => ExchangeCore(request, () => inner.ExchangeLogger(request, expectedResponseLength));
    private byte[] ExchangeCore(byte[] request, Func<byte[]> exchange)
    {
        string hex = Convert.ToHexString(request);
        if (!allowed.Contains(hex) && !(Mode01LoggerProtocol.IsAllowed(request) && request.Skip(1).All(id => allowed.Contains("2200" + id.ToString("X2")))))
            throw new InvalidOperationException("Logging test blocked a non-logging request before hardware I/O.");
        Requests.Add(hex); return exchange();
    }
    public void Dispose() { Disposed = true; inner.Dispose(); }
}

public partial class MainWindow
{
    internal async Task<Dictionary<string, object?>> ExerciseLoggerHardware(string port, string rom, string screenshot, bool tactrix, bool realEcu = false, int performanceSeconds = 0, int soakSeconds = 0)
    {
        Show(); UpdateLayout(); AdapterBox.SelectedIndex = tactrix ? 1 : 0; PortBox.Text = port; BaudBox.SelectedItem = 115200;
        await ClickAndWait(ConnectButton);
        if (transport is null || identity is null) throw new InvalidOperationException("Bench UI identification failed: " + StatusText.Text);
        var serial = connection; var passThru = (transport is DiagnosticTransport captured ? captured.Inner : transport) as SpeedLink.J2534.J2534ReadTransport;
        BindLoggerRom(rom); Pages.SelectedIndex = 2;
        var guarded = new LoggerOnlyTestTransport(transport, loggerDefinition); transport = guarded;
        var retainedIdentity = identity; var retainedCalibration = loggerCalibration;
        foreach (var row in loggerRows) row.Selected = new[] { "engine_rpm", "boost_pressure", "actual_lambda", "fuel_rail_pressure", "knock_retard", "injector_duty_cycle" }.Contains(row.Id);
        RecordCsvBox.IsChecked = true; Controls(); bool locked = false;
        if (performanceSeconds > 0 && (int)LoggerRateBox.SelectedValue != 0) throw new InvalidOperationException("Performance test must use Max polling.");
        var cycleWatch = new System.Diagnostics.Stopwatch();
        var resourceSamples = new List<object>(); int cycleTarget = performanceSeconds; double nextResourceSample = 0;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (soakSeconds > 0 && cycleTarget == soakSeconds && cycleWatch.Elapsed.TotalSeconds >= nextResourceSample)
            {
                process.Refresh();
                resourceSamples.Add(new { seconds = cycleWatch.Elapsed.TotalSeconds, residentBytes = process.WorkingSet64,
                    privateBytes = process.PrivateMemorySize64, managedBytes = GC.GetTotalMemory(false), handles = process.HandleCount,
                    allocatedBytes = GC.GetTotalAllocatedBytes(false), cpuSeconds = process.TotalProcessorTime.TotalSeconds });
                nextResourceSample += 5;
            }
            locked |= loggingActive && !LoggerRomButton.IsEnabled && !ReadButton.IsEnabled;
            if (loggerScans >= (cycleTarget > 0 ? 1 : 5) && (cycleTarget == 0 || cycleWatch.Elapsed.TotalSeconds >= cycleTarget) && LoggerStopButton.IsEnabled)
            { timer.Stop(); LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        var cycleReports = new List<object>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            cycleTarget = realEcu && cycle == 2 && soakSeconds > 0 ? soakSeconds : performanceSeconds;
            if (performanceSeconds > 0 && cycle == 0) foreach (var row in loggerRows) row.Selected = row.Id == "engine_rpm";
            if (performanceSeconds > 0 && cycle == 1) foreach (var row in loggerRows) row.Selected = new[] { "engine_rpm", "boost_pressure", "actual_lambda", "fuel_rail_pressure", "knock_retard", "injector_duty_cycle" }.Contains(row.Id);
            if (realEcu && cycle == 2)
            {
                foreach (var row in loggerRows) { row.Selected = true; row.Plotted = false; }
                foreach (var row in loggerRows.Take(6)) row.Plotted = true;
            }
            var choices = loggerRows.Select(r => (r.Id, r.Selected, r.Unit, r.Plotted)).ToArray();
            cycleWatch.Restart(); timer.Start();
            try
            {
                var work = ClickAndWait(LoggerStartButton);
                if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(Math.Max(45, cycleTarget + 20)))) != work)
                { stop?.Cancel(); await work; throw new InvalidOperationException("Physical logger cycle timed out."); }
                await work;
            }
            finally { timer.Stop(); }
            if (!ReferenceEquals(transport, guarded) || !ReferenceEquals(identity, retainedIdentity) || !ReferenceEquals(loggerCalibration, retainedCalibration) ||
                loggingActive || loggerScans < 5 || !locked || !LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled || ReadButton.IsEnabled ||
                !choices.SequenceEqual(loggerRows.Select(r => (r.Id, r.Selected, r.Unit, r.Plotted))))
                throw new InvalidOperationException("Physical logger could not restart after normal Stop: " + StatusText.Text);
            cycleReports.Add(new { cycle = cycle + 1, scans = loggerScans, selected = loggerRows.Count(r => r.Selected), restartEnabled = LoggerStartButton.IsEnabled,
                elapsedSeconds = cycleWatch.Elapsed.TotalSeconds, maximumPolling = (int)LoggerRateBox.SelectedValue == 0 });
        }
        await ClickAndWait(DisconnectButton);
        if (transport is not null || identity is not null || LoggerStartButton.IsEnabled || !guarded.Disposed || guarded.ReadAttempted)
            throw new InvalidOperationException("Physical logger disconnect or read-only guard failed.");
        if (serial is not null && (serial.IsOpen || !serial.LatencyRestored || serial.CleanupWarning is not null)) throw new InvalidOperationException("Physical logger serial cleanup failed.");
        if (passThru?.CloseWarning is not null) throw new InvalidOperationException("Physical logger J2534 cleanup failed.");
        string[] recordings = System.IO.Directory.GetDirectories(LoggerFolder); if (recordings.Length != 3) throw new InvalidOperationException("Unexpected physical UI recording count.");
        foreach (string recording in recordings)
        {
            var loaded = RecordedLog.Load(System.IO.Path.Combine(recording, "log.csv"));
            if (loaded.Rows < 5) throw new InvalidOperationException("Physical UI recording failed replay validation.");
        }
        UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = System.IO.File.Create(screenshot)) encoder.Save(output);
        if (diagnosticSession is null) throw new InvalidOperationException("Physical UI workflow did not capture diagnostics.");
        await diagnosticSession.FlushAsync();
        string support = System.IO.Path.ChangeExtension(screenshot, ".support.zip"); SupportReport.Export(diagnosticSession.Folder, support);
        return new() { ["passed"] = true, ["physicalAdapters"] = true, ["realEcuTested"] = realEcu, ["supportReport"] = support,
            ["adapter"] = tactrix ? "Tactrix" : "vLinker", ["calibration"] = loggerDefinition.SoftwareIds.Single(), ["cycles"] = cycleReports,
            ["actualButtons"] = true, ["restartWithoutReconnectVerified"] = true, ["connectionReleased"] = true,
            ["requests"] = guarded.Requests, ["onlyIdentificationAndLoggerQueries"] = true, ["liveProgrammingEnabled"] = false,
            ["sensorAccuracyValidated"] = false, ["recordings"] = recordings, ["replayLoaded"] = true, ["resourceSamples"] = resourceSamples, ["screenshot"] = screenshot };
    }
    internal async Task<Dictionary<string, object?>> ExerciseLoggerOffline(IReadTransport peer, string screenshot, bool faultExpected, LoggerDefinition profile, LoggerCalibration calibration, Action allowFixtureTraffic, string? romPath = null, Action? changeIdentity = null)
    {
        void Capture()
        {
            UpdateLayout(); DrawLoggerChart(); UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(screenshot); encoder.Save(file);
        }
        Show(); UpdateLayout(); Pages.SelectedIndex = 2;
        loggerCalibration = LoggerCalibration.ExplicitMap(99, 9); // Previous profile must never supply the next calibration.
        ConfigureLoggerProfile(profile);
        if (loggerCalibration is not null || loggerRows.Count != profile.ChannelIds.Count) throw new InvalidOperationException("Profile selection retained an old calibration or channel set.");
        string completeIdentity = RomInspector.KnownProfiles.Single(p => p.Calibration == profile.SoftwareIds.Single()).Identity;
        transport = peer; identity = new("test", peer.Adapter, null, completeIdentity);
        LoggerIdentityChanged();
        if (loggerDefinition.Id != profile.Id) throw new InvalidOperationException("Complete ECU identity selected the wrong logger.");
        if (romPath is null)
        {
            loggerCalibration = calibration;
            LoggerRomText.Text = profile.SoftwareIds.Single() + " · offline fixture calibration";
        }
        else
        {
            BindLoggerRom(romPath);
            if (loggerCalibration?.SourceRomSha256 != RomInspector.Hash(System.IO.File.ReadAllBytes(romPath)))
                throw new InvalidOperationException("Matching ECU backup was not bound to the logger.");
        }
        foreach (var row in loggerRows) { row.Selected = true; row.Plotted = false; }
        foreach (var row in loggerRows.Take(7)) row.Plotted = true;
        if (loggerRows.Count(r => r.Plotted) != 6) throw new InvalidOperationException("Live plot limit was not enforced.");
        var savedPlotIds = loggerRows.Where(r => r.Plotted).Select(r => r.Id).Order().ToArray();
        SaveLoggerSettings();
        foreach (var row in loggerRows) row.Plotted = new[] { "engine_rpm", "boost_pressure", "actual_lambda" }.Contains(row.Id);
        LoadLoggerSettings();
        if (!savedPlotIds.SequenceEqual(loggerRows.Where(r => r.Plotted).Select(r => r.Id).Order())) throw new InvalidOperationException("Remembered plot selections changed on reload.");
        foreach (var row in loggerRows) row.Plotted = new[] { "engine_rpm", "boost_pressure", "actual_lambda" }.Contains(row.Id);
        LoggerRateBox.SelectedValue = 10; RecordCsvBox.IsChecked = true;
        LoggerGrid.SelectedItem = loggerRows.First(row => row.Id == "engine_rpm"); Controls();
        bool locked = false;
        var retainedCalibration = loggerCalibration;
        var retainedChoices = loggerRows.Select(r => (r.Id, r.Selected, r.Unit, r.Plotted)).ToArray();
        int cycles = faultExpected ? 1 : 3;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (loggingActive) locked |= !ReadButton.IsEnabled && !LoggerRomButton.IsEnabled && !ViewRecordingButton.IsEnabled && loggerRows.All(row => !row.CanEdit);
            if (loggerScans >= 3 && LoggerStopButton.IsEnabled) { timer.Stop(); Capture(); LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            if (!LoggerStartButton.IsEnabled) throw new InvalidOperationException("Start logging was disabled before cycle " + (cycle + 1));
            timer.Start();
            try
            {
                LoggerStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Capture the locked controls before a fast fixture failure can finish between timer ticks.
                locked |= loggingActive && !ReadButton.IsEnabled && !LoggerRomButton.IsEnabled && loggerRows.All(row => !row.CanEdit);
                allowFixtureTraffic();
                var work = activeWork ?? throw new InvalidOperationException("Logger button did not start work.");
                if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(15))) != work)
                { stop?.Cancel(); await work; throw new InvalidOperationException("Offline UI logger timed out."); }
                await work;
            }
            finally { timer.Stop(); }
            if (!faultExpected && (!ReferenceEquals(transport, peer) || identity is null || !LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled || ReadButton.IsEnabled || loggingActive || busy || loggerScans < 3 ||
                !ReferenceEquals(loggerCalibration, retainedCalibration) || !retainedChoices.SequenceEqual(loggerRows.Select(r => (r.Id, r.Selected, r.Unit, r.Plotted)))))
                throw new InvalidOperationException("Normal Stop did not retain a restartable connection, choices and ROM binding in cycle " + (cycle + 1));
            if (!faultExpected)
            {
                if (!ViewRecordingButton.IsEnabled || latestRecordingPath is null) throw new InvalidOperationException("Latest recording shortcut is unavailable after Stop.");
                ViewRecordingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int attempt = 0; openingLatestRecording && attempt < 500; attempt++) await Task.Delay(10);
                if (openingLatestRecording || Pages.SelectedIndex != 3 || RecordingsPane.OpenedPath != latestRecordingPath || RecordingsPane.Recording?.Rows < 3)
                    throw new InvalidOperationException("View recording did not open the just-completed session.");
                Navigate(2);
            }
        }
        if (faultExpected && (transport is not null || identity is not null || loggingActive || LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled))
            throw new InvalidOperationException("Logger failure did not release the connection and disable restart.");
        string[] sessions = System.IO.Directory.GetDirectories(LoggerFolder);
        if (sessions.Length != cycles) throw new InvalidOperationException("Expected a separate recording per logging cycle.");
        foreach (string session in sessions)
        {
            using var saved = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(session, "session.json")));
            if (!faultExpected && (saved.RootElement.GetProperty("status").GetString() != "Stopped" || saved.RootElement.GetProperty("rows").GetInt64() < 3))
                throw new InvalidOperationException("A restarted logging session did not finalize its own recording.");
            if (!faultExpected && RecordedLog.Load(System.IO.Path.Combine(session, "log.csv")).Rows < 3)
                throw new InvalidOperationException("A restarted recording could not be opened.");
        }
        string folder = sessions.Order(StringComparer.Ordinal).Last();
        using var manifest = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(folder, "session.json")));
        string status = manifest.RootElement.GetProperty("status").GetString()!;
        long rows = manifest.RootElement.GetProperty("rows").GetInt64();
        if (faultExpected ? !status.StartsWith("Stopped after error:", StringComparison.Ordinal) : rows < 3 || status != "Stopped")
            throw new InvalidOperationException("Unexpected logger recording completion: " + status);
        if (!locked || !System.IO.File.Exists(System.IO.Path.Combine(vault, "logger-settings.json")) || store.Scan(vault).Count != 0)
            throw new InvalidOperationException("Logger locking, remembered settings or backup separation failed.");
        if (peer.ReadAttempted) throw new InvalidOperationException("Logger entered a ROM-read workflow.");
        UpdateLayout(); DrawLoggerChart();
        if (!faultExpected && !LoggerPlots.Children.OfType<LiveLoggerChart>().Any(c => c.PointCount >= 2)) throw new InvalidOperationException("Rolling chart was not drawn.");
        if (faultExpected) Capture();
        else
        {
            string savedPath = latestRecordingPath!;
            var previousReplay = RecordingsPane.Recording;
            latestRecordingPath = savedPath + ".missing";
            ViewRecordingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; openingLatestRecording && attempt < 500; attempt++) await Task.Delay(10);
            if (openingLatestRecording || !StatusText.Text.StartsWith("Recording could not be opened:", StringComparison.Ordinal) ||
                !ReferenceEquals(previousReplay, RecordingsPane.Recording) || !ViewRecordingButton.IsEnabled)
                throw new InvalidOperationException("A moved recording broke the viewer or shortcut.");
            latestRecordingPath = savedPath;
            ViewRecordingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; openingLatestRecording && attempt < 500; attempt++) await Task.Delay(10);
            if (openingLatestRecording || RecordingsPane.OpenedPath != savedPath || !StatusText.Text.StartsWith("Recording opened ·", StringComparison.Ordinal))
                throw new InvalidOperationException("The recording shortcut did not recover after a missing file.");
            Navigate(2);
            // Stop immediately after Start as well; cancellation before the first poll must remain restartable.
            string? lastRecorded = latestRecordingPath;
            RecordCsvBox.IsChecked = false;
            LoggerStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await activeWork!;
            if (!ReferenceEquals(transport, peer) || !LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled)
                throw new InvalidOperationException("Immediate Start/Stop lost the healthy connection.");
            timer.Start();
            try { await ClickAndWait(LoggerStartButton); }
            finally { timer.Stop(); }
            if (!ReferenceEquals(transport, peer) || !LoggerStartButton.IsEnabled || loggerScans < 3 || System.IO.Directory.GetDirectories(LoggerFolder).Length != cycles)
                throw new InvalidOperationException("Live-only logging did not restart after immediate Stop without creating a recording.");
            if (latestRecordingPath != lastRecorded || !ViewRecordingButton.IsEnabled)
                throw new InvalidOperationException("Live-only logging lost the latest saved recording.");
            if (changeIdentity is not null)
            {
                changeIdentity();
                await ClickAndWait(LoggerStartButton);
                if (!StatusText.Text.Contains("ECU identity changed", StringComparison.Ordinal) || System.IO.Directory.GetDirectories(LoggerFolder).Length != cycles)
                    throw new InvalidOperationException("Changed ECU identity did not stop before a new recording.");
            }
            else await ClickAndWait(DisconnectButton);
            if (transport is not null || identity is not null || LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled)
                throw new InvalidOperationException("Explicit disconnect did not release the retained connection.");
        }
        if (LoggerGrid.ActualHeight < 150 || LoggerPlots.ActualHeight < 100) throw new InvalidOperationException("Logger table or chart collapsed.");
        Width = MinWidth; Height = MinHeight; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (ActualWidth > MinWidth || ActualHeight > MinHeight || LoggerGrid.ActualHeight < 149 || LoggerFileText.TransformToAncestor(this).Transform(new Point(0, 0)).Y > ActualHeight - 65)
            throw new InvalidOperationException("Logger controls are clipped at the minimum window size.");
        var report = new Dictionary<string, object?>
        {
            ["passed"] = true, ["mode"] = "offline UI fixture, no hardware", ["channels"] = loggerRows.Count,
            ["actualStartStopButtonEvents"] = true, ["readAndSettingsLockedDuringLogging"] = locked,
            ["recordingRows"] = rows, ["recordingStatus"] = status, ["folder"] = folder,
            ["connectionReleased"] = true, ["settingsSaved"] = true, ["faultExpected"] = faultExpected,
            ["startStopCycles"] = cycles, ["restartWithoutReconnectVerified"] = !faultExpected,
            ["immediateStopVerified"] = !faultExpected, ["separateRecordingsVerified"] = true,
            ["recordingOffRestartVerified"] = !faultExpected, ["identityChangeRejected"] = changeIdentity is not null,
            ["latestRecordingShortcutVerified"] = !faultExpected,
            ["minimumWindowLayoutVerified"] = true, ["sixPlotLimitVerified"] = true,
            ["calibration"] = profile.SoftwareIds.Single(), ["profile"] = profile.Id,
            ["ecuCalibration"] = completeIdentity, ["matchingRomBound"] = romPath is not null,
            ["profileSwitchClearedCalibration"] = true,
            ["realEcuTested"] = false, ["screenshot"] = screenshot
        };
        Close(); return report;
    }
    private async Task ClickAndWait(Button button)
    {
        if (!button.IsEnabled)
            throw new InvalidOperationException("Control unexpectedly disabled: " + button.Name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await (activeWork ?? throw new InvalidOperationException("Control did not start an operation."));
    }
    internal async Task<Dictionary<string, object?>> ExerciseHardware(string port, byte[] expected, int initialBaud = 115200, bool tactrix = false)
    {
        Show();
        UpdateLayout();
        AdapterBox.SelectedIndex = tactrix ? 1 : 0;
        PortBox.Text = port;
        BaudBox.SelectedItem = initialBaud;
        await ClickAndWait(ConnectButton);
        if (transport is null || identity is null)
            throw new InvalidOperationException("UI identification failed: " + StatusText.Text);
        var cancelledConnection = connection;
        var cancelledTactrix = transport as SpeedLink.J2534.J2534ReadTransport;
        if (!tactrix && cancelledConnection is null)
            throw new InvalidOperationException("FTDI connection missing.");
        var identified = identity;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ProgressBar.Value >= 5 && StopButton.IsEnabled) { timer.Stop(); StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
        timer.Start();
        try
        {
            await ClickAndWait(ReadButton);
        }
        finally { timer.Stop(); }
        var stopped = store.Scan(vault);
        if (stopped.Count != 1 || stopped[0].Complete || !stopped[0].Valid || ReadButton.IsEnabled || StopButton.IsEnabled)
            throw new InvalidOperationException("UI cancellation or reconnect guard failed.");
        await ClickAndWait(DisconnectButton);
        if (cancelledConnection is not null && (cancelledConnection.IsOpen || !cancelledConnection.LatencyRestored || cancelledConnection.CleanupWarning is not null || cancelledConnection.DriverErrors.Length != 0))
            throw new InvalidOperationException("FTDI cancellation cleanup failed.");
        if (cancelledTactrix?.CloseWarning is not null)
            throw new InvalidOperationException("Tactrix cancellation cleanup failed: " + cancelledTactrix.CloseWarning);
        await ClickAndWait(ConnectButton);
        if (transport is null || identity is null || !identity.MatchesVehicle(identified))
            throw new InvalidOperationException("UI reconnect failed.");
        var completedConnection = connection;
        var completedTactrix = transport as SpeedLink.J2534.J2534ReadTransport;
        if (!tactrix && completedConnection is null)
            throw new InvalidOperationException("FTDI connection missing after reconnect.");
        await ClickAndWait(ReadButton);
        var completed = store.Scan(vault).Where(row => row.Complete).ToArray();
        if (completed.Length != 1)
            throw new InvalidOperationException("UI did not complete a verified backup: " + StatusText.Text);
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(completed[0].Path)!, "rom.bin");
        var loaded = store.Load(path, "hardware");
        if (!loaded.Data.AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("UI wire capture differs from reference.");
        await ClickAndWait(DisconnectButton);
        if (completedConnection is not null && (completedConnection.IsOpen || !completedConnection.LatencyRestored || completedConnection.CleanupWarning is not null || completedConnection.DriverErrors.Length != 0))
            throw new InvalidOperationException("FTDI full-read cleanup failed.");
        if (completedTactrix?.CloseWarning is not null)
            throw new InvalidOperationException("Tactrix full-read cleanup failed: " + completedTactrix.CloseWarning);
        var report = new Dictionary<string, object?>
        {
            ["passed"] = true,
            ["actualButtonEvents"] = true,
            ["identify"] = true,
            ["partialReadCancellation"] = true,
            ["reconnect"] = true,
            ["twoFullMatchingReads"] = true,
            ["serialDisconnected"] = transport is null,
            ["selectedAdapter"] = tactrix ? "Tactrix Openport 2.0" : "Vgate vLinker FS",
            ["selectedDriver"] = (DriverBox.SelectedItem as SpeedLink.J2534.J2534Driver)?.Name,
            ["availableDrivers"] = DriverBox.Items.Cast<SpeedLink.J2534.J2534Driver>().Select(item => item.Name).ToArray(),
            ["serialBackend"] = tactrix ? "J2534 ISO15765" : SpeedLink.Hardware.FtdiSerialConnection.Backend,
            ["transferBaud"] = VLinkerReadTransport.TransferBaud,
            ["driverErrors"] = completedConnection?.DriverErrors ?? [],
            ["latencyRestored"] = completedConnection is not null && cancelledConnection is not null ? completedConnection.LatencyRestored && cancelledConnection.LatencyRestored : (bool?)null,
            ["backupPath"] = path,
            ["sha256"] = RomInspector.Hash(loaded.Data),
            ["stoppedOperation"] = stopped[0].Path,
            ["simulatorAvailable"] = false,
            ["liveProgrammingEnabled"] = false
        };
        Close();
        return report;
    }
}
