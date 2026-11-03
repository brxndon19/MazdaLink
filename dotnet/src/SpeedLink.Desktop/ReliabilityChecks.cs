using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<object> ExerciseReliabilityOffline(string rom, string folder, string recording, Func<LoggerDefinition, IReadTransport> createPeer)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException(name); cases.Add(name); }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        Show(); Navigate(1);
        cases.AddRange(await EditorPane.ExerciseRejectedRomFiles(rom, folder));
        OpenEditorFile(rom); await Idle();
        var editor = EditorPane;
        var map = editor.Document!.Definition.Tables.First(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice);
        editor.OpenTable(map);
        editor.PopBrowserForTest(); editor.PopGraphForTest(); editor.DetailsButton.IsChecked = true;
        await Idle();
        Check(editor.FloatingVisibilityForTest().SequenceEqual(new[] { true, true, true }), "Floating panels visible in Editor");
        foreach (int page in new[] { 0, 2, 3, 4, 5 })
        {
            Navigate(page); await Idle();
            Check(editor.FloatingVisibilityForTest().All(v => !v), "Floating panels hidden in workspace " + page);
        }
        Navigate(1); await Idle();
        Check(editor.FloatingVisibilityForTest().All(v => v), "Floating panels restored on return");
        editor.MapTabs.Items.Clear(); await Idle();
        Check(!editor.FloatingVisibilityForTest()[1] && !editor.FloatingVisibilityForTest()[2] && editor.GraphFloating, "Empty floating graph and information hidden without losing placement");
        editor.OpenTable(map); await Idle();
        Check(editor.FloatingVisibilityForTest().Take(2).All(v => v), "Floating browser and graph restored with reopened map");
        string? errorText = null;
        EditorWorkspace.ExecuteEditorAction(() => throw new UnauthorizedAccessException("Controlled access denial"), m => errorText = m);
        Check(errorText == "Controlled access denial" && IsVisible, "Access denial reaches recoverable editor error boundary");

        var profile = LoggerDefinition.ForCalibration(editor.Document.Definition.Calibration)!;
        ConfigureLoggerProfile(profile); loggerCalibration = LoggerCalibration.ExplicitMap(100, 0);
        using var peer = new CountingTestTransport(createPeer(profile)); transport = peer; identity = new("test", peer.Adapter, null, RomInspector.KnownProfiles.Single(p => p.Calibration == profile.SoftwareIds.Single()).Identity);
        foreach (var row in loggerRows) { row.Selected = row.Id == "engine_rpm"; row.Plotted = row.Selected; }
        StoichBox.Text = "invalid"; Navigate(2); Controls();
        LoggerStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!busy && !loggingActive && ReferenceEquals(transport, peer) && activeWork is null && peer.Requests == 0 && !peer.Disposed && StatusText.Text.Contains("between 1 and 30", StringComparison.Ordinal), "Invalid AFR keeps connection and issues no logging requests");
        var lambda = loggerRows.Single(r => r.Id == "actual_lambda");
        lambda.Selected = true; lambda.Unit = "AFR"; StoichBox.Text = "";
        LoggerStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!loggingActive && ReferenceEquals(transport, peer) && peer.Requests == 0 && !peer.Disposed && activeWork is null, "Missing AFR conversion input is recoverable without traffic");
        lambda.Unit = "lambda"; lambda.Selected = false;
        peer.ReadAttempted = true; Controls();
        Check(!LoggerStartButton.IsEnabled && LoggerConnectionHint.IsVisible && LoggerConnectionButton.IsEnabled && LoggerConnectionHint.Text.Contains("reconnect", StringComparison.Ordinal), "Read session explains reconnect before logging");
        Width = MinWidth; Height = MinHeight; await Idle();
        var hintBounds = LoggerConnectionButton.TransformToAncestor(this).TransformBounds(new Rect(LoggerConnectionButton.RenderSize));
        Check(hintBounds.Right <= ActualWidth && hintBounds.Bottom <= ActualHeight && LoggerGrid.ActualHeight >= 149, "Post-read guidance fits minimum window size");
        Width = 1280; Height = 720;
        LoggerConnectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Pages.SelectedIndex == 0 && ReferenceEquals(transport, peer), "Reconnect guidance navigates without changing session");
        peer.ReadAttempted = false; StoichBox.Text = "14.7"; RecordCsvBox.IsChecked = true; Navigate(2); Controls();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (loggerScans >= 3) { timer.Stop(); LoggerStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
        timer.Start();
        try
        {
            LoggerStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var work = activeWork ?? throw new InvalidOperationException("Corrected AFR did not start logging.");
            if (await Task.WhenAny(work, Task.Delay(10000)) != work) { stop?.Cancel(); await work; throw new InvalidOperationException("Logging test timed out."); }
            await work;
        }
        finally { timer.Stop(); }
        string[] plotted = LoggerPlots.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Select(t => t.Text).ToArray();
        Check(ReferenceEquals(transport, peer) && LoggerStartButton.IsEnabled && !LoggerStopButton.IsEnabled && !ReadButton.IsEnabled && loggerScans >= 3 && plotted.Any(t => t.StartsWith("Stopped", StringComparison.Ordinal)) && !plotted.Any(t => t.StartsWith("OK", StringComparison.Ordinal)), "Stopped charts refresh immediately and logging can restart without reconnecting");
        await ClickAndWait(DisconnectButton);
        Check(transport is null && !LoggerStartButton.IsEnabled && peer.Disposed, "Explicit disconnect releases the stopped logger connection");
        var display = new LoggerRow(profile.Channels.First(c => c.Id == "engine_rpm")); var now = DateTimeOffset.UtcNow;
        display.Observe(new(display.Id, 1000, now, "OK"), profile, null, now);
        display.Observe(new(display.Id, null, null, "Waiting"), profile, null, now.AddMilliseconds(100));
        Check(display.ValueText == "1000" && display.Status.StartsWith("Pending", StringComparison.Ordinal), "Pending scan retains explicitly aged fresh value");
        display.Observe(new(display.Id, null, null, "Waiting"), profile, null, now.AddSeconds(6));
        Check(display.ValueText == "—" && display.Status.StartsWith("Stale", StringComparison.Ordinal), "Pending value expires at freshness limit");
        display.Observe(new(display.Id, 1000, now.AddSeconds(7), "OK"), profile, null, now.AddSeconds(7));
        display.Observe(new(display.Id, null, null, "Unavailable: NRC"), profile, null, now.AddSeconds(7));
        display.Observe(new(display.Id, null, null, "Waiting"), profile, null, now.AddSeconds(7));
        Check(display.ValueText == "—", "Rejected data is never revived by a pending scan");
        display.Observe(new(display.Id, 1000, now.AddSeconds(8), "OK"), profile, null, now.AddSeconds(8));
        Check(display.ExpireIfStale(now.AddSeconds(14)) && display.ValueText == "—" && display.Status.StartsWith("Stale", StringComparison.Ordinal), "A stalled receive expires displayed values without waiting for the next reply");

        Navigate(3); await RecordingsPane.OpenAsync(recording);
        var retained = RecordingsPane.Recording;
        string bad = Path.Combine(folder, "invalid.csv"); File.WriteAllText(bad, "invalid");
        try { await RecordingsPane.OpenAsync(bad); throw new InvalidOperationException("Bad recording accepted."); }
        catch (InvalidDataException) { }
        Check(ReferenceEquals(retained, RecordingsPane.Recording) && !RecordingsPane.Summary.Text.Contains("Opening", StringComparison.Ordinal) && RecordingsPane.OpenButton.IsEnabled, "Failed replacement retains recording and restores usable status");
        var replay = await RecordingsPane.ExerciseOffline(recording, Path.Combine(folder, "replay.png"));
        Check((bool)replay["passed"]!, "Replay defaults and six-plot limit");

        var gen2 = LoggerDefinition.ForCalibration("L3CGEE000")!;
        using (var replayPeer = createPeer(gen2))
        {
            var cal = LoggerCalibration.ExplicitMap(100, 0);
            var ecu = new EcuIdentity("test", replayPeer.Adapter, null, "L3CGEE000");
            var live = new LiveLogger(gen2, cal, replayPeer, ecu, gen2.ChannelIds);
            using var file = new LoggerRecording(Path.Combine(folder, "gen2-recording"), gen2, cal, ecu, gen2.ChannelIds, gen2.Channels.ToDictionary(c => c.Id, c => c.Unit), null, Version, 5);
            for (int i = 0; i < 3; i++) file.Append(live.ReadFrame());
            file.Complete("Stopped");
            var result = await RecordingsPane.ExerciseOffline(Path.Combine(file.Folder, "log.csv"), Path.Combine(folder, "gen2-replay.png"));
            Check((bool)result["passed"]!, "Gen 2 replay accepts five preferred defaults and six selectable plots");
        }
        ConfigureLoggerProfile(profile);
        for (int i = 0; i < loggerRows.Count; i++)
        {
            var row = loggerRows[i]; row.Selected = i % 3 != 0; row.Plotted = row.Selected && i % 5 == 0;
            row.Unit = row.Units.FirstOrDefault(u => u != row.Channel.Unit) ?? row.Channel.Unit;
        }
        var expectedProfile = loggerRows.Select(r => new LoggerChannelPreference(r.Id, r.Selected, r.Unit, r.Plotted)).ToArray();
        ConfigureLoggerProfile(gen2);
        foreach (var row in loggerRows) { row.Selected = row.Id is "absolute_throttle_position" or "short_term_fuel_trim" or "battery_voltage" or "calculated_load"; row.Plotted = row.Id == "battery_voltage"; }
        LoggerRateBox.SelectedValue = 20; StoichBox.Text = "14.0"; RecordCsvBox.IsChecked = false;
        var expected = loggerRows.Select(r => new LoggerChannelPreference(r.Id, r.Selected, r.Unit, r.Plotted)).ToArray();
        string settingsVault = vault;
        CloseWorkbenchTest(); // Uses the normal Window.Closing settings path without starting another log.
        var restored = new MainWindow(settingsVault, editor.TestInspector, editor.TestGuard);
        try
        {
            restored.Show();
            Check(restored.loggerDefinition.Id == gen2.Id && Choices(restored).SequenceEqual(expected), "Offline close and fresh launch retain every Gen 2 selection, unit and plot");
            Check((int)restored.LoggerRateBox.SelectedValue == 20 && restored.StoichBox.Text == "14.0" && restored.RecordCsvBox.IsChecked == false && restored.loggerCalibration is null, "Global preferences restored without trusting old ROM binding");
            Check(!restored.EditorPane.FloatingVisibilityForTest()[1], "Startup without map hides remembered floating graph");
            restored.ConfigureLoggerProfile(profile);
            Check(Choices(restored).SequenceEqual(profile.Id == gen2.Id ? expected : expectedProfile), "Fresh launch retains every selection, unit and plot for " + profile.SoftwareIds.Single());
            restored.ConfigureLoggerProfile(gen2);
            Check(Choices(restored).SequenceEqual(expected), "Profile switching preserves independent selections, units and plots");
            restored.ConfigureLoggerProfile(profile);
        }
        finally { restored.CloseWorkbenchTest(); }
        var activeProfileRestart = new MainWindow(settingsVault, editor.TestInspector, editor.TestGuard);
        try
        {
            activeProfileRestart.Show();
            Check(activeProfileRestart.loggerDefinition.Id == profile.Id && Choices(activeProfileRestart).SequenceEqual(profile.Id == gen2.Id ? expected : expectedProfile), "Fresh launch automatically restores the active profile and all choices for " + profile.SoftwareIds.Single());
            Check((int)activeProfileRestart.LoggerRateBox.SelectedValue == 20 && activeProfileRestart.StoichBox.Text == "14.0" && activeProfileRestart.RecordCsvBox.IsChecked == false && activeProfileRestart.loggerCalibration is null, "Active-profile restart preserves global choices without an ECU binding");
        }
        finally { activeProfileRestart.CloseWorkbenchTest(); }
        return new { passed = true, cases, hardwareAccessed = false };
        static LoggerChannelPreference[] Choices(MainWindow host) => host.loggerRows.Select(r => new LoggerChannelPreference(r.Id, r.Selected, r.Unit, r.Plotted)).ToArray();
    }
    private sealed class CountingTestTransport(IReadTransport inner) : IReadTransport
    {
        public string Mode => inner.Mode;
        public string Adapter => inner.Adapter;
        public int ReadChunkSize => inner.ReadChunkSize;
        public bool ReadAttempted { get => inner.ReadAttempted; set => inner.ReadAttempted = value; }
        public int Requests { get; private set; }
        public bool Disposed { get; private set; }
        public byte[] Exchange(byte[] request) { Requests++; return inner.Exchange(request); }
        public void Dispose() { if (Disposed) return; Disposed = true; inner.Dispose(); }
    }
}

public partial class EditorWorkspace
{
    internal bool[] FloatingVisibilityForTest() => [browserWindow?.IsVisible == true, graphWindow?.IsVisible == true, infoWindow?.IsVisible == true];
    internal void PopBrowserForTest() => PopBrowser_Click(this, new());
    internal void PopGraphForTest() => PopGraph_Click(this, new());
}
