using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpeedLink.Core;
[assembly: InternalsVisibleTo("SpeedLink.UiHarness")]
namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<Dictionary<string, object?>> ExerciseLoggerHardware(string port, string rom, string screenshot, bool tactrix)
    {
        Show(); UpdateLayout(); AdapterBox.SelectedIndex = tactrix ? 1 : 0; PortBox.Text = port; BaudBox.SelectedItem = 115200;
        await ClickAndWait(ConnectButton);
        if (transport is null || identity is null) throw new InvalidOperationException("Bench UI identification failed: " + StatusText.Text);
        var serial = connection; var passThru = (transport is DiagnosticTransport captured ? captured.Inner : transport) as SpeedLink.J2534.J2534ReadTransport;
        BindLoggerRom(rom); Pages.SelectedIndex = 2;
        foreach (var row in loggerRows) row.Selected = new[] { "engine_rpm", "boost_pressure", "actual_lambda", "fuel_rail_pressure", "knock_retard", "injector_duty_cycle" }.Contains(row.Id);
        RecordCsvBox.IsChecked = true; Controls(); bool locked = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            locked |= loggingActive && !LoggerRomButton.IsEnabled && !ReadButton.IsEnabled;
            if (loggerScans >= 5 && LoggerStopButton.IsEnabled) { timer.Stop(); LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        timer.Start();
        try { await ClickAndWait(LoggerStartButton); }
        finally { timer.Stop(); }
        if (transport is not null || identity is not null || loggingActive || loggerScans < 5 || !locked) throw new InvalidOperationException("Physical logger UI workflow did not complete: " + StatusText.Text);
        if (serial is not null && (serial.IsOpen || !serial.LatencyRestored || serial.CleanupWarning is not null)) throw new InvalidOperationException("Physical logger serial cleanup failed.");
        if (passThru?.CloseWarning is not null) throw new InvalidOperationException("Physical logger J2534 cleanup failed.");
        string[] recordings = System.IO.Directory.GetDirectories(LoggerFolder); if (recordings.Length != 1) throw new InvalidOperationException("Unexpected physical UI recording count.");
        var loaded = RecordedLog.Load(System.IO.Path.Combine(recordings[0], "log.csv"));
        if (loaded.Rows < 5 || loaded.Channels.Count != 6) throw new InvalidOperationException("Physical UI recording failed replay validation.");
        UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = System.IO.File.Create(screenshot)) encoder.Save(output);
        if (diagnosticSession is null) throw new InvalidOperationException("Physical UI workflow did not capture diagnostics.");
        await diagnosticSession.FlushAsync();
        string support = System.IO.Path.ChangeExtension(screenshot, ".support.zip"); SupportReport.Export(diagnosticSession.Folder, support);
        return new() { ["passed"] = true, ["physicalAdapters"] = true, ["realEcuTested"] = false, ["supportReport"] = support,
            ["adapter"] = tactrix ? "Tactrix" : "vLinker", ["channels"] = 6, ["scans"] = loaded.Rows,
            ["actualButtons"] = true, ["connectionReleased"] = true, ["recording"] = recordings[0], ["replayLoaded"] = true, ["screenshot"] = screenshot };
    }
    internal async Task<Dictionary<string, object?>> ExerciseLoggerOffline(IReadTransport peer, string screenshot, bool faultExpected, LoggerDefinition profile, LoggerCalibration calibration, Action allowFixtureTraffic, string? romPath = null)
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
        LoggerRateBox.SelectedItem = 10; RecordCsvBox.IsChecked = true;
        LoggerGrid.SelectedItem = loggerRows.First(row => row.Id == "engine_rpm"); Controls();
        bool locked = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (loggingActive) locked |= !ReadButton.IsEnabled && !LoggerRomButton.IsEnabled && loggerRows.All(row => !row.CanEdit);
            if (loggerScans >= 3 && LoggerStopButton.IsEnabled) { timer.Stop(); Capture(); LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
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
        if (transport is not null || identity is not null || loggingActive || LoggerStartButton.IsEnabled || LoggerStopButton.IsEnabled)
            throw new InvalidOperationException("Logger did not release the connection and controls.");
        string[] sessions = System.IO.Directory.GetDirectories(LoggerFolder);
        if (sessions.Length != 1) throw new InvalidOperationException("Expected one CSV recording.");
        string folder = sessions[0];
        using var manifest = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(folder, "session.json")));
        string status = manifest.RootElement.GetProperty("status").GetString()!;
        long rows = manifest.RootElement.GetProperty("rows").GetInt64();
        if (faultExpected ? !status.StartsWith("Stopped after error:", StringComparison.Ordinal) : rows < 3 || status != "Stopped")
            throw new InvalidOperationException("Unexpected logger recording completion: " + status);
        if (!locked || !System.IO.File.Exists(System.IO.Path.Combine(vault, "logger-settings.json")) || store.Scan(vault).Count != 0)
            throw new InvalidOperationException("Logger locking, remembered settings or backup separation failed.");
        if (peer.ReadAttempted) throw new InvalidOperationException("Logger entered a ROM-read workflow.");
        UpdateLayout(); DrawLoggerChart();
        if (!faultExpected && !LoggerPlots.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<System.Windows.Controls.Canvas>()).Any(c => c.Children.OfType<System.Windows.Shapes.Polyline>().Any(line => line.Points.Count >= 2))) throw new InvalidOperationException("Rolling chart was not drawn.");
        if (faultExpected) Capture();
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
