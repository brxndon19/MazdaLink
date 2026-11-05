using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SpeedLink.Core;
using SpeedLink.Hardware;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

internal sealed class FlashWorkspace : UserControl
{
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    private const uint Continuous = 0x80000000, SystemRequired = 1;
    private readonly string vault;
    private readonly FlashBackupLibrary library;
    private readonly TextBox image = new();
    private readonly RadioButton writeMode = new() { Content = "Write ROM", IsChecked = true, Margin = new(0, 0, 18, 0) };
    private readonly RadioButton recoverMode = new() { Content = "Recover interrupted write" };
    private readonly ComboBox driver = new() { MinWidth = 300 };
    private readonly TextBlock connectionHint = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Amber, Visibility = Visibility.Collapsed };
    private readonly TextBlock backupText = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted };
    private readonly TextBlock probeStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new(8, 4, 0, 0) };
    private readonly Button checkEcu = new() { Content = "Check ECU state", IsEnabled = false };
    private readonly ComboBox savedRuns = new() { MinWidth = 220 };
    private string? selectedJournal;
    private readonly Button load = new() { Content = "Check image & backup" }, start = new() { Content = "Write ROM", IsEnabled = false }, stop = new() { Content = "Stop", IsEnabled = false };
    private readonly CheckBox ready = new() { Margin = new(0, 10, 0, 10) };
    private readonly TextBlock readyLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox readback = new() { MinWidth = 220, ItemsSource = new[] { "Two full readbacks (default)", "One full readback", "Skip readback" }, SelectedIndex = 0 };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap }, status = new() { TextWrapping = TextWrapping.Wrap }, detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted };
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 16, Margin = new(0, 10, 0, 10) };
    private readonly TextBox events = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 150 };
    private readonly List<Control> inputs = [];
    private BenchFlashPlan? plan;
    private BenchRecoveryPlan? recovery;
    private BenchFlashWorkflow? workflow;
    private CancellationTokenSource? cancel;
    private bool active, loading, checking, identifying, connectionUnavailable, recoveryConfirmed;
    private string? preferredBackup;
    internal string? PreferredBackup => preferredBackup;
    private bool IsRecovery => recoverMode.IsChecked == true;
    internal bool HardwareInUse => active || checking || identifying;
    internal bool IsWorking => HardwareInUse || loading;
    internal event Action? HardwareUseChanged;
    internal event Action? ConnectionRequested;
    private bool uiStateFailed;
    private readonly Func<IBenchFlashTransport>? offlineFactory;
    private readonly (RomInspector Inspector, TuningCodeGuard CodeGuard)? simulation;
    private readonly string evidenceMode;
    private sealed record VLinkerPort(string Name)
    {
        public override string ToString() => $"vLinker FS ({Name})";
    }
    internal FlashWorkspace(string vault, FlashBackupLibrary library, Func<IBenchFlashTransport>? offlineFactory = null,
        (RomInspector Inspector, TuningCodeGuard CodeGuard)? simulation = null)
    {
        UiTheme.Ensure();
        if (simulation.HasValue && offlineFactory is null) throw new ArgumentException("Simulation requires an offline transport.");
        this.vault = vault; this.library = library; this.offlineFactory = offlineFactory; this.simulation = simulation;
        evidenceMode = offlineFactory is null ? "hardware" : "simulator";
        Background = UiTheme.Panel; Foreground = UiTheme.Text;
        var top = new StackPanel { Margin = new(20), MaxWidth = 1050, HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        top.Children.Add(new TextBlock { Text = "Flash & Recover", FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Gen 1 only · Tactrix or vLinker FS · Mongoose-Plus ISO/CAN3 experimental", Foreground = UiTheme.Amber, Margin = new(0, 5, 0, 8), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Preserves the first 32 KiB. Erases and rewrites the complete application. Requires a verified original SpeedLink backup. After reset, each readback compares every byte with the intended image.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 5, 0, 8) }; top.Children.Add(modes);
        modes.Children.Add(writeMode); modes.Children.Add(recoverMode); inputs.AddRange([writeMode, recoverMode]);
        var connectionRow = new DockPanel(); top.Children.Add(connectionRow);
        var goToConnection = new Button { Content = "Go to Connection", Margin = new(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        goToConnection.Click += (_, _) => ConnectionRequested?.Invoke();
        connectionRow.Children.Add(goToConnection);
        connectionRow.Children.Add(connectionHint);
        connectionHint.IsVisibleChanged += (_, _) => goToConnection.Visibility = connectionHint.Visibility;
        var drivers = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(drivers);
        drivers.Children.Add(new TextBlock { Text = "Adapter", Width = 180, VerticalAlignment = VerticalAlignment.Center });
        var refreshAdapters = new Button { Content = "Refresh devices", Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(refreshAdapters, Dock.Right);
        refreshAdapters.Click += (_, _) => RefreshAdapters();
        drivers.Children.Add(refreshAdapters); drivers.Children.Add(driver); inputs.AddRange([driver, refreshAdapters]);
        RefreshAdapters();
        var probeRow = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(probeRow);
        probeRow.Children.Add(checkEcu); probeRow.Children.Add(probeStatus);
        checkEcu.Click += async (_, _) => await ProbeEcu();
        var imageRow = PathRow(top, "ROM to write", image);
        var runRow = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(runRow);
        runRow.Children.Add(new TextBlock { Text = "Saved interrupted runs", Width = 180, VerticalAlignment = VerticalAlignment.Center });
        runRow.Children.Add(savedRuns); inputs.Add(savedRuns);
        var browseRun = new Button { Content = "Browse older run…", Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(browseRun, Dock.Right);
        runRow.Children.Insert(1, browseRun);
        inputs.Add(browseRun);
        browseRun.Click += (_, _) => BrowseInterruptedRun();
        savedRuns.SelectionChanged += async (_, _) =>
        {
            if (savedRuns.SelectedItem is InterruptedRun run) await SelectInterruptedRun(run.Journal);
        };
        var backupRow = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(backupRow);
        var addBackupSource = new Button { Content = "Find backup folder…", Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(addBackupSource, Dock.Right);
        addBackupSource.Click += async (_, _) => await FindBackupFolder();
        backupRow.Children.Add(addBackupSource); backupRow.Children.Add(backupText); inputs.Add(addBackupSource);
        var readbackRow = new DockPanel { Margin = new(0, 8, 0, 6) };
        readbackRow.Children.Add(new TextBlock { Text = "After writing", Width = 180, VerticalAlignment = VerticalAlignment.Center });
        readbackRow.Children.Add(readback);
        void ChangeMode()
        {
            imageRow.Visibility = IsRecovery ? Visibility.Collapsed : Visibility.Visible;
            runRow.Visibility = probeRow.Visibility = IsRecovery ? Visibility.Visible : Visibility.Collapsed;
            readbackRow.Visibility = IsRecovery ? Visibility.Collapsed : Visibility.Visible;
            load.Visibility = IsRecovery ? Visibility.Collapsed : Visibility.Visible;
            load.Content = "Identify ECU & find backup";
            start.Content = IsRecovery ? "Recover stock" : "Write ROM";
            ResetProbe();
            if (IsRecovery) RefreshInterruptedRuns();
            InvalidatePlan();
        }
        writeMode.Checked += (_, _) => ChangeMode();
        recoverMode.Checked += (_, _) => ChangeMode();
        load.Margin = new(0, 8, 0, 8); load.Click += Load_Click; top.Children.Add(load); inputs.Add(load);
        ready.Content = readyLabel;
        summary.Margin = new(0, 6, 0, 6); top.Children.Add(summary); top.Children.Add(ready);
        top.Children.Add(readbackRow); inputs.Add(readback);
        readback.SelectionChanged += (_, _) => { ready.IsChecked = false; Refresh(); };
        ready.Checked += (_, _) => Refresh(); ready.Unchecked += (_, _) => Refresh();
        image.TextChanged += (_, _) => InvalidatePlan();
        driver.SelectionChanged += (_, _) => { ResetProbe(); InvalidatePlan(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; top.Children.Add(actions);
        start.MinWidth = 145; start.Margin = new(0, 0, 8, 0); start.Click += Start_Click; actions.Children.Add(start);
        stop.MinWidth = 80; stop.Click += (_, _) => { cancel?.Cancel(); stop.IsEnabled = false; }; actions.Children.Add(stop);
        var open = new Button { Content = "Open run folder", Margin = new(8, 0, 0, 0) };
        open.Click += (_, _) => { if (workflow?.RunFolder is string folder) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }; actions.Children.Add(open);
        top.Children.Add(bar); status.FontWeight = FontWeights.SemiBold; top.Children.Add(status); detail.Margin = new(0, 6, 0, 10); top.Children.Add(detail);
        events.MaxHeight = 220;
        top.Children.Add(events);
        ChangeMode();
    }
    private DockPanel PathRow(Panel parent, string label, TextBox box, string filter = "ROM files|*.bin;*.rom|All files|*.*")
    {
        var row = new DockPanel { Margin = new(0, 5, 0, 5) }; parent.Children.Add(row);
        row.Children.Add(new TextBlock { Text = label, Width = 180, VerticalAlignment = VerticalAlignment.Center });
        var browse = new Button { Content = "Browse…", Margin = new(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = filter }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) box.Text = dialog.FileName; };
        row.Children.Add(box); inputs.Add(box); inputs.Add(browse);
        return row;
    }
    internal void UseBackup(string path, string? port, J2534Driver? selectedDriver)
    {
        if (IsWorking) return;
        preferredBackup = path;
        writeMode.IsChecked = true;
        driver.SelectedItem = driver.Items.Cast<object>().FirstOrDefault(item => item switch
        {
            J2534Driver j2534 => selectedDriver is not null && j2534.Kind == selectedDriver.Kind &&
                j2534.LibraryPath.Equals(selectedDriver.LibraryPath, StringComparison.OrdinalIgnoreCase),
            VLinkerPort serial => port is not null && serial.Name.Equals(port, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
        InvalidatePlan();
    }
    internal void SetConnectionUnavailable(bool unavailable)
    {
        if (connectionUnavailable == unavailable) return;
        connectionUnavailable = unavailable;
        connectionHint.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        connectionHint.Text = "Finish the current task and disconnect the adapter in Connection before using it here.";
        ResetProbe();
        if (!IsWorking) InvalidatePlan();
        Refresh();
    }
    internal void ShowWorkingMessage()
        => status.Text = "Wait for the current flash or diagnostic operation. Keep power and USB connected.";

    private void InvalidatePlan()
    {
        if (IsWorking) return;
        plan = null; recovery = null; summary.Text = ""; ready.IsChecked = false;
        backupText.Text = preferredBackup is null ? "SpeedLink will find the verified original backup." :
            "Backup from Connection · " + preferredBackup;
        status.Text = IsRecovery ? "Choose a saved interrupted run and check ECU state." : "Select the ROM to write and an adapter.";
        Refresh();
    }

    private void ResetProbe()
    {
        recoveryConfirmed = false;
        probeStatus.Text = "Select an adapter to check for the Gen 1 recovery bootloader.";
    }

    private void RefreshAdapters()
    {
        object? previous = driver.SelectedItem;
        driver.ItemsSource = offlineFactory is null
            ? J2534Drivers.Discover().Where(d => d.Kind == J2534AdapterKind.Tactrix).Cast<object>()
                .Concat(J2534Drivers.Discover(J2534AdapterKind.MongoosePlusIsoCan3)
                    .Select(d => d with { Name = d.Name + " · experimental" }))
                .Concat(SerialConnection.GetPorts().Select(port => new VLinkerPort(port))).ToArray()
            : [new VLinkerPort("COM99"), new J2534Driver("Synthetic Mongoose-Plus ISO/CAN3", "unused.dll", J2534AdapterKind.MongoosePlusIsoCan3)];
        driver.SelectedItem = previous;
    }

    private sealed record InterruptedRun(string Journal, string Caption)
    {
        public override string ToString() => Caption;
    }

    private InterruptedRun DescribeRun(string journal)
    {
        var (calibration, _) = BenchRecoveryPlan.Describe(journal, evidenceMode);
        return new(journal, $"{File.GetLastWriteTime(journal):g} · {calibration}");
    }

    private void RefreshInterruptedRuns()
    {
        var runs = new List<InterruptedRun>();
        foreach (string journal in library.InterruptedJournals(vault, evidenceMode))
        {
            try { runs.Add(DescribeRun(journal)); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        savedRuns.ItemsSource = runs;
        savedRuns.SelectedItem = runs.FirstOrDefault(run => run.Journal == selectedJournal);
        if (selectedJournal is null && runs.Count == 1) savedRuns.SelectedIndex = 0;
    }

    private async Task SelectInterruptedRun(string journal)
    {
        selectedJournal = journal;
        InvalidatePlan();
        if (recoveryConfirmed) await LoadPlan();
    }

    private void BrowseInterruptedRun()
    {
        var dialog = new OpenFolderDialog { Title = "Select the interrupted SpeedLink flash run folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string journal = Path.Combine(dialog.FolderName, "journal.jsonl");
        InterruptedRun run;
        try { run = DescribeRun(journal); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            status.Text = "Could not open this interrupted run · " + error.Message;
            return;
        }
        var runs = savedRuns.Items.OfType<InterruptedRun>().Where(entry => entry.Journal != journal).Prepend(run).ToArray();
        savedRuns.ItemsSource = runs;
        // The selection event resolves the matching backup when boot mode is confirmed.
        savedRuns.SelectedItem = run;
    }

    private async Task FindBackupFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select a previous SpeedLink data folder or a verified backup folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            library.Remember(dialog.FolderName);
            InvalidatePlan();
            backupText.Text = "Backup folder added · " + dialog.FolderName;
            if (IsRecovery)
            {
                RefreshInterruptedRuns();
                if (recoveryConfirmed && !loading && selectedJournal is not null) await LoadPlan();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { backupText.Text = "Could not add the backup folder · " + error.Message; }
    }

    private void Refresh()
    {
        bool idle = !IsWorking;
        bool adapterReady = idle && !connectionUnavailable && driver.SelectedItem is J2534Driver or VLinkerPort;
        foreach (var control in inputs) control.IsEnabled = idle;
        load.IsEnabled = IsRecovery ? idle && selectedJournal is not null : adapterReady && image.Text.Length > 0;
        checkEcu.IsEnabled = IsRecovery && adapterReady;
        bool qualified = plan is not null && (offlineFactory is not null || BenchFlashWorkflow.IsNativeWriterVerified(plan.Profile.Calibration));
        const string setup = "Ignition is on, engine is off, and stable power and USB will stay connected";
        readyLabel.Text = IsRecovery ? "Same physical ECU as the interrupted run. " + setup : setup;
        ready.IsEnabled = adapterReady && qualified && (!IsRecovery || recoveryConfirmed);
        start.IsEnabled = ready.IsEnabled && ready.IsChecked == true;
        if (!active) stop.IsEnabled = false;
    }

    private Func<IBenchFlashTransport> SelectedTransport() => offlineFactory ?? (driver.SelectedItem switch
    {
        J2534Driver j2534 => () => BenchFlashTransport.Create(j2534),
        VLinkerPort vlinker => () => new VLinkerBenchFlashTransport(new FtdiSerialConnection(vlinker.Name, 115200)),
        _ => throw new InvalidOperationException("Select an adapter.")
    });

    private T WithAdapter<T>(Func<T> action)
    {
        string name = offlineFactory is null ? "Local\\SpeedLink.LocalBenchFlash" : "Local\\SpeedLink.OfflineBenchFlash." + Guid.NewGuid().ToString("N");
        using var mutex = new Mutex(false, name);
        if (!mutex.WaitOne(0)) throw new InvalidOperationException("Another SpeedLink programming operation is already active.");
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private async Task ProbeEcu()
    {
        if (!checkEcu.IsEnabled) return;
        var factory = SelectedTransport();
        recoveryConfirmed = false;
        ready.IsChecked = false;
        checking = true;
        probeStatus.Text = "Checking for the Gen 1 recovery bootloader…";
        HardwareUseChanged?.Invoke(); Refresh();
        try
        {
            recoveryConfirmed = await Task.Run(() => WithAdapter(() => BenchFlashWorkflow.CheckRecoveryState(factory, evidenceMode)))
                == FlashEcuState.Recovery;
            probeStatus.Text = recoveryConfirmed ? "Recovery mode confirmed. Review the journal and original backup." :
                "Normal mode. If the previous write stopped during verification, check the ROM from Connection.";
        }
        catch (Exception error) { probeStatus.Text = "ECU state unconfirmed · " + error.Message; }
        finally { checking = false; HardwareUseChanged?.Invoke(); Refresh(); }
        if (recoveryConfirmed && recovery is null) await LoadPlan();
    }

    private async Task LoadPlan()
    {
        if (!load.IsEnabled) return;
        plan = null; recovery = null; ready.IsChecked = false; loading = true; Refresh();
        try
        {
            string target = image.Text.Trim(), prior = selectedJournal ?? "";
            bool restoring = IsRecovery;
            EcuIdentity? live = null;
            if (!restoring)
            {
                if (!File.Exists(target) || new FileInfo(target).Length != RomInspector.RomSize)
                    throw new InvalidDataException("Select a complete 1 MiB ROM to write.");
                var factory = SelectedTransport();
                identifying = true;
                status.Text = "Identifying the ECU and checking that the engine is off…";
                HardwareUseChanged?.Invoke();
                live = await Task.Run(() => WithAdapter(() => BenchFlashWorkflow.IdentifyForBackup(factory, evidenceMode)));
            }
            status.Text = "Finding the original verified backup…";
            (plan, recovery) = await Task.Run(() => FindPlan(restoring, target, prior, live));
            backupText.Text = "Verified original backup · " + plan.BackupPath;
            summary.Text = $"{plan.FullCalibration}\n{plan.ChangedBytes:N0} changed bytes · {BenchFlashPlan.Length:N0} bytes to write · 256 blocks\nImage SHA-256: {plan.ImageSha256}";
            status.Text = !BenchFlashWorkflow.IsNativeWriterVerified(plan.Profile.Calibration) ? "Files verified. This profile is waiting for native writer qualification." :
                recovery is null ? "Files verified. The live ECU, RPM, supply and retained boot are checked before erase." :
                "The interrupted journal matches this original backup. Recovery checks for boot-only replies and restores stock from the first block.";
        }
        catch (Exception error) { summary.Text = ""; status.Text = "Image/backup rejected: " + error.Message; }
        finally { loading = false; identifying = false; HardwareUseChanged?.Invoke(); Refresh(); }
    }

    private (BenchFlashPlan Plan, BenchRecoveryPlan? Recovery) FindPlan(bool restoring, string target, string prior, EcuIdentity? live)
    {
        string? hash = restoring ? BenchRecoveryPlan.Describe(prior, evidenceMode).BackupHash : null;
        string? failure = null;
        foreach (string original in library.Candidates(evidenceMode, live, hash, preferredBackup))
        {
            try
            {
                if (restoring)
                {
                    var restore = simulation is { } fixture
                        ? BenchRecoveryPlan.LoadSimulation(prior, original, fixture.Inspector, fixture.CodeGuard)
                        : BenchRecoveryPlan.Load(prior, original, evidenceMode);
                    return (restore.Stock, restore);
                }
                var write = simulation is { } test
                    ? BenchFlashPlan.LoadSimulation(target, original, test.Inspector, test.CodeGuard)
                    : BenchFlashPlan.Load(target, original, evidenceMode);
                if (live is not null && write.MatchesLive(live)) return (write, null);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            { failure ??= error.Message; }
        }
        throw new InvalidDataException(failure ?? (restoring
            ? "The exact original backup for this run was not found. Use Find backup folder to locate its original SpeedLink folder."
            : "No verified backup matched this ECU's VIN or chassis ID and calibration. Use Find backup folder or read a verified backup from Connection."));
    }
    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadPlan();
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!start.IsEnabled || plan is not { } prepared) return;
        var preparedRecovery = recovery;
        var factory = SelectedTransport();
        bool verifyOnce = readback.SelectedIndex == 1;
        bool skipReadback = preparedRecovery is null && readback.SelectedIndex == 2;
        string prompt = preparedRecovery is null ? "Write the selected ROM?" : "Restore the original backup to the same ECU?";
        prompt += "\n\nKeep the ignition on and the engine off. Keep the battery support supply and USB connected through erase, transfer and reset.";
        if (skipReadback) prompt += "\n\nThe ECU will reconnect after reset. The written ROM will remain unverified.";
        if (offlineFactory is null && MessageBox.Show(Window.GetWindow(this), prompt,
                "Confirm Gen 1 programming", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        active = true; cancel?.Dispose(); cancel = new(); workflow = new(resetWait: offlineFactory is null ? null : _ => { });
        if (preparedRecovery is not null) probeStatus.Text = "Rechecking ECU recovery mode before erase…";
        HardwareUseChanged?.Invoke();
        bar.Value = 0; events.Clear(); lastStage = null; uiStateFailed = false; Refresh();
        var progress = new WindowProgress(this);
        try
        {
            await Task.Run(() => WithAdapter(() =>
            {
                bool holdingWake = false;
                try
                {
                    if (offlineFactory is null)
                    {
                        holdingWake = SetThreadExecutionState(Continuous | SystemRequired) != 0;
                        if (!holdingWake) throw new IOException("Windows could not keep the PC awake during programming.");
                    }
                    Directory.CreateDirectory(vault);
                    if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(vault))!).AvailableFreeSpace < 64 * 1024 * 1024)
                        throw new IOException("At least 64 MiB of free space is required for durable write and readback evidence.");
                    return preparedRecovery is null ? workflow.Execute(prepared, factory, vault, progress, cancel.Token, evidenceMode, verifyOnce, skipReadback) :
                        workflow.Recover(preparedRecovery, samePhysicalEcuConfirmed: true, factory, vault, progress, cancel.Token, evidenceMode);
                }
                finally
                {
                    if (holdingWake) SetThreadExecutionState(Continuous);
                }
            }));
        }
        catch (Exception error)
        {
            status.Text = $"{workflow.Stage}: {error.Message}";
            detail.Text = workflow.Stage switch
            {
                BenchFlashStage.VerificationRequired => "The reset was acknowledged. Read and verify a backup, then compare it with the intended image before driving. Keep this run folder.",
                BenchFlashStage.RecoveryRequired => "Keep the ECU connected. Retain this run folder and original backup, then follow the Gen 1 recovery guide.",
                _ => "Stopped before application erase. Review the reported preflight problem."
            };
        }
        finally
        {
            // Drain queued progress before enabling another attempt.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            active = false; ready.IsChecked = false;
            cancel?.Dispose(); cancel = null;
            if (preparedRecovery is not null)
            {
                recoveryConfirmed = false;
                probeStatus.Text = workflow.Stage == BenchFlashStage.Complete ? "Stock recovery verified." :
                    "Check ECU state before another recovery attempt.";
            }
            HardwareUseChanged?.Invoke(); Refresh();
        }
    }
    private BenchFlashStage? lastStage;
    private sealed class WindowProgress(FlashWorkspace window) : IProgress<BenchFlashProgress>
    {
        public void Report(BenchFlashProgress value)
        {
            // Render the non-cancellable boundary before the erase may be dispatched.
            if (value.Stage == BenchFlashStage.Erasing) window.Dispatcher.Invoke(() => window.ShowProgress(value));
            else window.Dispatcher.BeginInvoke(() => window.ShowProgress(value));
        }
    }
    private void ShowProgress(BenchFlashProgress value)
    {
        if (lastStage is { } previous && value.Stage < previous) return;
        bar.Value = value.Fraction * 100; status.Text = value.Message;
        if (IsRecovery && value.Stage == BenchFlashStage.ProgrammingSession)
            probeStatus.Text = "Recovery mode confirmed again before erase.";
        stop.IsEnabled = active && value.CanCancel && cancel?.IsCancellationRequested != true;
        if (active && (start.IsEnabled || inputs.Any(c => c.IsEnabled) || !value.CanCancel && stop.IsEnabled)) uiStateFailed = true;
        detail.Text = $"{value.Stage} · {value.AcknowledgedBytes:N0} / {BenchFlashPlan.Length:N0} acknowledged write bytes\n{workflow?.RunFolder}";
        if (lastStage != value.Stage) { events.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + value.Stage + " · " + value.Message + Environment.NewLine); events.ScrollToEnd(); lastStage = value.Stage; }
    }
    internal async Task<object> Smoke(string original, string output, BenchFlashUiPeerState peer)
    {
        string moved = Path.Combine(output, "Moved target.bin");
        File.Copy(original, moved);
        image.Text = moved;
        if (load.IsEnabled || start.IsEnabled) throw new InvalidOperationException("An adapter is required.");
        driver.SelectedItem = driver.Items.OfType<VLinkerPort>().Single();
        await LoadPlan();
        if (plan?.BackupPath != original || start.IsEnabled)
            throw new InvalidOperationException("Automatic lookup did not select the original backup or require setup confirmation: " + status.Text);
        ready.IsChecked = true;
        if (!start.IsEnabled) throw new InvalidOperationException("Verified write could not be armed.");
        image.Text += ".missing";
        if (plan is not null || start.IsEnabled) throw new InvalidOperationException("Changing an input retained a stale plan.");
        await LoadPlan();
        if (plan is not null || start.IsEnabled) throw new InvalidOperationException("A missing image was accepted.");

        image.Text = moved;
        await LoadPlan();
        if (plan is null || readback.SelectedIndex != 0)
            throw new InvalidOperationException("Rejected image workflow did not recover or two reads were not the default.");
        readback.SelectedIndex = 1;
        ready.IsChecked = true;
        await RunSmokeOperation();
        if (workflow?.Stage != BenchFlashStage.Complete || bar.Value != 100 || uiStateFailed || start.IsEnabled)
            throw new InvalidOperationException("One-read write failed: " + status.Text);
        CheckReadbackCount("verified-result.json", 1);
        string completed = Path.Combine(workflow.RunFolder!, "journal.jsonl");

        recoverMode.IsChecked = true;
        await ProbeEcu();
        if (!probeStatus.Text.StartsWith("Normal mode", StringComparison.Ordinal) || recoveryConfirmed)
            throw new InvalidOperationException("A normal ECU was not recognized.");
        peer.ProbesUnavailable = true;
        try { await ProbeEcu(); }
        finally { peer.ProbesUnavailable = false; }
        if (!probeStatus.Text.StartsWith("ECU state unconfirmed", StringComparison.Ordinal) || recoveryConfirmed)
            throw new InvalidOperationException("A silent ECU was accepted.");
        await SelectInterruptedRun(completed);
        await LoadPlan();
        if (plan is not null || start.IsEnabled) throw new InvalidOperationException("A completed journal was accepted.");

        writeMode.IsChecked = true;
        driver.SelectedItem = driver.Items.OfType<J2534Driver>().Single(d => d.Experimental);
        await LoadPlan();
        if (plan is null) throw new InvalidOperationException("Experimental adapter could not find the backup: " + status.Text);
        readback.SelectedIndex = 2;
        ready.IsChecked = true;
        await RunSmokeOperation();
        if (workflow?.Stage != BenchFlashStage.WrittenUnverified || bar.Value != 100 || uiStateFailed ||
            File.Exists(Path.Combine(workflow.RunFolder!, "verified-result.json")) ||
            Directory.Exists(Path.Combine(workflow.RunFolder!, "readback")))
            throw new InvalidOperationException("Skip readback did not report an unverified write.");
        CheckReadbackCount("unverified-result.json", 0);

        ready.IsChecked = true;
        peer.SimulateInterruptedWrite();
        await RunSmokeOperation();
        if (workflow?.Stage != BenchFlashStage.RecoveryRequired || start.IsEnabled)
            throw new InvalidOperationException("Interrupted write did not enter recovery-required state.");
        string interrupted = Path.Combine(workflow.RunFolder!, "journal.jsonl");
        recoverMode.IsChecked = true;
        savedRuns.SelectedItem = savedRuns.Items.OfType<InterruptedRun>().Single(run => run.Journal == interrupted);
        if (recovery is not null || ready.IsEnabled || start.IsEnabled)
            throw new InvalidOperationException("The ECU-state gate failed: " + status.Text);
        await ProbeEcu();
        if (!recoveryConfirmed || recovery?.Stock.BackupPath != original || !ready.IsEnabled || start.IsEnabled)
            throw new InvalidOperationException("Bootloader probe did not find the original backup and enable setup confirmation: " + status.Text);
        ready.IsChecked = true;
        await RunSmokeOperation();
        if (workflow?.Stage != BenchFlashStage.Complete || uiStateFailed || start.IsEnabled)
            throw new InvalidOperationException("Offline stock recovery failed: " + status.Text);
        CheckReadbackCount("verified-result.json", 2);

        return new { passed = true, hardwareAccessed = false, planValidated = true, explicitSetupConfirmation = true, changedInputDisarms = true,
            rejectedInputRecovers = true, vlinkerSelectionVerified = true, offlineTransferVerified = true, offlineStockRecoveryVerified = true,
            mongooseRecoverySelectionVerified = driver.SelectedItem is J2534Driver { Experimental: true }, progressAndLockingVerified = !uiStateFailed,
            movedTargetAndAutomaticBackup = true, recoveryStateGate = true, skipReadbackVerified = true, target = plan?.FullCalibration };

        void CheckReadbackCount(string filename, int expected)
        {
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(workflow!.RunFolder!, filename)));
            if (result.RootElement.GetProperty("readbackPasses").GetInt32() != expected)
                throw new InvalidOperationException("The operation did not use the selected readback policy.");
        }

        async Task RunSmokeOperation()
        {
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var watch = Stopwatch.StartNew();
            while (active && watch.Elapsed.TotalSeconds < 35) await Task.Delay(10);
            if (active) throw new TimeoutException("Offline desktop operation did not finish.");
        }
    }
}
