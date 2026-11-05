using System.ComponentModel;
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

public partial class MainWindow
{
    private void Gen1Flash_Click(object sender, RoutedEventArgs e)
    {
        if (busy || transport is not null || closing)
        { MessageBox.Show(this, "Stop the current task and disconnect the adapter in SpeedLink before opening Gen 1 flashing.", "Gen 1 flashing"); return; }
        new BenchFlashWindow(Path.Combine(vault, "flashing")) { Owner = this }.ShowDialog();
    }
}

internal sealed class BenchFlashWindow : Window
{
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    private const uint Continuous = 0x80000000, SystemRequired = 1;
    private readonly string vault;
    private readonly TextBox image = new() { MinWidth = 300 }, backup = new() { MinWidth = 300 }, interruptedJournal = new() { MinWidth = 300 };
    private readonly ComboBox operation = new() { MinWidth = 300, ItemsSource = new[] { "Write ROM", "Recover interrupted write to stock" }, SelectedIndex = 0 };
    private readonly ComboBox driver = new() { MinWidth = 300 };
    private readonly Button load = new() { Content = "Check image & backup" }, start = new() { Content = "Write ROM", IsEnabled = false }, stop = new() { Content = "Stop", IsEnabled = false };
    private readonly CheckBox ready = new() { Margin = new(0, 10, 0, 10) };
    private readonly TextBlock readyLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox singleReadback = new() { Content = "One full readback instead of two", Margin = new(0, 8, 0, 2) };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap }, status = new() { TextWrapping = TextWrapping.Wrap }, detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted };
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 16, Margin = new(0, 10, 0, 10) };
    private readonly TextBox events = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 150 };
    private readonly List<Control> inputs = [];
    private BenchFlashPlan? plan;
    private BenchRecoveryPlan? recovery;
    private BenchFlashWorkflow? workflow;
    private CancellationTokenSource? cancel;
    private bool active, loading;
    private bool uiStateFailed;
    private readonly Func<IBenchFlashTransport>? offlineFactory;
    private readonly (RomInspector Inspector, TuningCodeGuard CodeGuard)? simulation;
    private readonly string evidenceMode;
    private sealed record VLinkerPort(string Name)
    {
        public override string ToString() => $"vLinker FS ({Name})";
    }
    internal BenchFlashWindow(string vault, Func<IBenchFlashTransport>? offlineFactory = null,
        (RomInspector Inspector, TuningCodeGuard CodeGuard)? simulation = null)
    {
        UiTheme.Ensure();
        if (simulation.HasValue && offlineFactory is null) throw new ArgumentException("Simulation requires an offline transport.");
        this.vault = vault; this.offlineFactory = offlineFactory; this.simulation = simulation;
        evidenceMode = offlineFactory is null ? "hardware" : "simulator";
        Title = "SpeedLink · Gen 1 flashing and recovery";
        Width = 930; Height = 800; MinWidth = 700; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiTheme.Panel; Foreground = UiTheme.Text;
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Gen 1 flashing and recovery", FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Gen 1 only · Tactrix or vLinker FS · Mongoose-Plus ISO/CAN3 experimental", Foreground = UiTheme.Amber, Margin = new(0, 5, 0, 8), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Preserves the first 32 KiB. Erases and rewrites the complete application. Requires a verified original SpeedLink backup. After reset, each readback compares every byte with the intended image.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var modes = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(modes);
        modes.Children.Add(new TextBlock { Text = "Operation", Width = 180, VerticalAlignment = VerticalAlignment.Center });
        modes.Children.Add(operation); inputs.Add(operation);
        var imageRow = PathRow(top, "ROM to write", image);
        var journalRow = PathRow(top, "Interrupted flash journal", interruptedJournal, "Flash journal|journal.jsonl");
        journalRow.Visibility = Visibility.Collapsed;
        PathRow(top, "Original backup rom.bin", backup);
        operation.SelectionChanged += (_, _) =>
        {
            bool restoring = operation.SelectedIndex == 1;
            imageRow.Visibility = restoring ? Visibility.Collapsed : Visibility.Visible;
            journalRow.Visibility = restoring ? Visibility.Visible : Visibility.Collapsed;
            singleReadback.Visibility = restoring ? Visibility.Collapsed : Visibility.Visible;
            load.Content = restoring ? "Check journal & backup" : "Check image & backup";
            start.Content = restoring ? "Recover stock" : "Write ROM";
            InvalidatePlan();
        };
        var drivers = new DockPanel { Margin = new(0, 5, 0, 5) }; top.Children.Add(drivers);
        drivers.Children.Add(new TextBlock { Text = "Adapter", Width = 180, VerticalAlignment = VerticalAlignment.Center }); drivers.Children.Add(driver); inputs.Add(driver);
        driver.ItemsSource = offlineFactory is null
            ? J2534Drivers.Discover().Where(d => d.Kind == J2534AdapterKind.Tactrix).Cast<object>()
                .Concat(J2534Drivers.Discover(J2534AdapterKind.MongoosePlusIsoCan3)
                    .Select(d => d with { Name = d.Name + " · experimental" }))
                .Concat(SerialConnection.GetPorts().Select(port => new VLinkerPort(port))).ToArray()
            : [new VLinkerPort("COM99"), new J2534Driver("Synthetic Mongoose-Plus ISO/CAN3", "unused.dll", J2534AdapterKind.MongoosePlusIsoCan3)];
        driver.SelectedIndex = -1;
        load.Margin = new(0, 8, 0, 8); load.Click += Load_Click; top.Children.Add(load); inputs.Add(load);
        ready.Content = readyLabel;
        summary.Margin = new(0, 6, 0, 6); top.Children.Add(summary); top.Children.Add(ready);
        top.Children.Add(singleReadback); inputs.Add(singleReadback);
        ready.Checked += (_, _) => Refresh(); ready.Unchecked += (_, _) => Refresh();
        image.TextChanged += (_, _) => InvalidatePlan(); backup.TextChanged += (_, _) => InvalidatePlan();
        interruptedJournal.TextChanged += (_, _) => InvalidatePlan();
        driver.SelectionChanged += (_, _) => { ready.IsChecked = false; Refresh(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; top.Children.Add(actions);
        start.MinWidth = 145; start.Margin = new(0, 0, 8, 0); start.Click += Start_Click; actions.Children.Add(start);
        stop.MinWidth = 80; stop.Click += (_, _) => { cancel?.Cancel(); stop.IsEnabled = false; }; actions.Children.Add(stop);
        var open = new Button { Content = "Open run folder", Margin = new(8, 0, 0, 0) };
        open.Click += (_, _) => { if (workflow?.RunFolder is string folder) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }; actions.Children.Add(open);
        top.Children.Add(bar); status.FontWeight = FontWeights.SemiBold; top.Children.Add(status); detail.Margin = new(0, 6, 0, 10); top.Children.Add(detail);
        root.Children.Add(events);
        status.Text = "Choose the ROM image and its verified original backup. No adapter is opened by Check image & backup.";
        Closing += Window_Closing;
        Refresh();
    }
    private DockPanel PathRow(Panel parent, string label, TextBox box, string filter = "ROM files|*.bin;*.rom|All files|*.*")
    {
        var row = new DockPanel { Margin = new(0, 5, 0, 5) }; parent.Children.Add(row);
        row.Children.Add(new TextBlock { Text = label, Width = 180, VerticalAlignment = VerticalAlignment.Center });
        var browse = new Button { Content = "Browse…", Margin = new(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = filter }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; };
        row.Children.Add(box); inputs.Add(box); inputs.Add(browse);
        return row;
    }
    private void InvalidatePlan() { if (active) return; plan = null; recovery = null; summary.Text = ""; ready.IsChecked = false; Refresh(); }
    private void Refresh()
    {
        foreach (var control in inputs) control.IsEnabled = !active && !loading;
        bool qualified = plan is not null && (offlineFactory is not null || BenchFlashWorkflow.IsNativeWriterVerified(plan.Profile.Calibration));
        const string setup = "Ignition is on, engine is off, and stable power and USB will stay connected";
        readyLabel.Text = operation.SelectedIndex == 1 ? "Same physical ECU as the interrupted run. " + setup : setup;
        ready.IsEnabled = !active && !loading && qualified;
        start.IsEnabled = ready.IsEnabled && ready.IsChecked == true && driver.SelectedItem is J2534Driver or VLinkerPort;
        if (!active) stop.IsEnabled = false;
    }
    private async Task LoadPlan()
    {
        plan = null; recovery = null; ready.IsChecked = false; loading = true; Refresh();
        try
        {
            string target = image.Text, original = backup.Text, prior = interruptedJournal.Text;
            if (operation.SelectedIndex == 1)
            {
                recovery = await Task.Run(() => simulation is { } fixture
                    ? BenchRecoveryPlan.LoadSimulation(prior, original, fixture.Inspector, fixture.CodeGuard)
                    : BenchRecoveryPlan.Load(prior, original, evidenceMode));
                plan = recovery.Stock;
            }
            else plan = await Task.Run(() => simulation is { } fixture
                ? BenchFlashPlan.LoadSimulation(target, original, fixture.Inspector, fixture.CodeGuard)
                : BenchFlashPlan.Load(target, original, evidenceMode));
            summary.Text = $"{plan.FullCalibration}\n{plan.ChangedBytes:N0} changed bytes · {BenchFlashPlan.Length:N0} bytes to write · 256 blocks\nImage SHA-256: {plan.ImageSha256}";
            status.Text = !BenchFlashWorkflow.IsNativeWriterVerified(plan.Profile.Calibration) ? "Files verified. This profile is waiting for native writer qualification." :
                recovery is null ? "Files verified. The live ECU, RPM, supply and retained boot are checked before erase." :
                "The interrupted journal matches this original backup. Recovery checks for boot-only replies and restores stock from the first block.";
        }
        catch (Exception error) { summary.Text = ""; status.Text = "Image/backup rejected: " + error.Message; }
        finally { loading = false; Refresh(); }
    }
    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadPlan();
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!start.IsEnabled || plan is not { } prepared) return;
        var preparedRecovery = recovery;
        Func<IBenchFlashTransport> factory = offlineFactory ?? (driver.SelectedItem switch
        {
            J2534Driver tactrix => () => BenchFlashTransport.Create(tactrix),
            VLinkerPort vlinker => () => new VLinkerBenchFlashTransport(new FtdiSerialConnection(vlinker.Name, 115200)),
            _ => throw new InvalidOperationException("Select a bench adapter.")
        });
        bool verifyOnce = singleReadback.IsChecked == true;
        string prompt = preparedRecovery is null ? "Write the selected ROM?" : "Restore the original backup to the same ECU?";
        prompt += "\n\nKeep the ignition on and the engine off. Keep the battery support supply and USB connected through erase, transfer and reset.";
        if (offlineFactory is null && MessageBox.Show(this, prompt,
                "Confirm Gen 1 programming", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        active = true; cancel?.Dispose(); cancel = new(); workflow = new(resetWait: offlineFactory is null ? null : _ => { });
        bar.Value = 0; events.Clear(); lastStage = null; uiStateFailed = false; Refresh();
        var progress = new WindowProgress(this);
        try
        {
            await Task.Run(() =>
            {
                string mutexName = offlineFactory is null ? "Local\\SpeedLink.LocalBenchFlash" : "Local\\SpeedLink.OfflineBenchFlash." + Guid.NewGuid().ToString("N");
                using var mutex = new Mutex(false, mutexName);
                if (!mutex.WaitOne(0)) throw new InvalidOperationException("Another SpeedLink programming operation is already active.");
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
                    return preparedRecovery is null ? workflow.Execute(prepared, factory, vault, progress, cancel.Token, evidenceMode, verifyOnce) :
                        workflow.Recover(preparedRecovery, samePhysicalEcuConfirmed: true, factory, vault, progress, cancel.Token, evidenceMode);
                }
                finally
                {
                    if (holdingWake) SetThreadExecutionState(Continuous);
                    mutex.ReleaseMutex();
                }
            });
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
            active = false; ready.IsChecked = false; Refresh();
        }
    }
    private BenchFlashStage? lastStage;
    private sealed class WindowProgress(BenchFlashWindow window) : IProgress<BenchFlashProgress>
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
        stop.IsEnabled = active && value.CanCancel && cancel?.IsCancellationRequested != true;
        if (active && (start.IsEnabled || inputs.Any(c => c.IsEnabled) || !value.CanCancel && stop.IsEnabled)) uiStateFailed = true;
        detail.Text = $"{value.Stage} · {value.AcknowledgedBytes:N0} / {BenchFlashPlan.Length:N0} acknowledged write bytes\n{workflow?.RunFolder}";
        if (lastStage != value.Stage) { events.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + value.Stage + " · " + value.Message + Environment.NewLine); events.ScrollToEnd(); lastStage = value.Stage; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!active && !loading) { cancel?.Dispose(); return; }
        e.Cancel = true;
        status.Text = "An operation is active. Use Stop when available and wait for the current exchange. Keep power and USB connected.";
    }
    internal async Task<object> Smoke(string imagePath, string backupPath, string output, Action simulateInterruptedWrite)
    {
        Show(); image.Text = imagePath; backup.Text = backupPath; await LoadPlan();
        if (plan is null || start.IsEnabled) throw new InvalidOperationException("Plan did not load or Start was armed without setup confirmation.");
        ready.IsChecked = true; Refresh();
        if (start.IsEnabled) throw new InvalidOperationException("Start was armed without an adapter selection.");
        driver.SelectedItem = driver.Items.OfType<VLinkerPort>().Single();
        ready.IsChecked = true;
        if (!start.IsEnabled) throw new InvalidOperationException("Valid plan could not be armed with vLinker.");
        image.Text += ".missing";
        if (plan is not null || start.IsEnabled) throw new InvalidOperationException("Changing an input retained a stale armed plan.");
        await LoadPlan(); if (plan is not null || start.IsEnabled) throw new InvalidOperationException("A missing image was accepted.");
        image.Text = imagePath; await LoadPlan(); if (plan is null) throw new InvalidOperationException("Rejected-image workflow did not recover.");
        ready.IsChecked = true; Refresh(); UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "local-flash-preview.png"))) encoder.Save(file);
        if (offlineFactory is not null)
        {
            if (singleReadback.IsChecked == true) throw new InvalidOperationException("Two readbacks must remain the default.");
            singleReadback.IsChecked = true;
            await RunSmokeOperation();
            if (active || workflow?.Stage != BenchFlashStage.Complete || bar.Value != 100 || uiStateFailed || start.IsEnabled || stop.IsEnabled)
                throw new InvalidOperationException("Offline desktop flash workflow failed: " + status.Text);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(workflow.RunFolder!, "verified-result.json")));
            if (result.RootElement.GetProperty("readbackPasses").GetInt32() != 1)
                throw new InvalidOperationException("The desktop selection did not enable one-pass readback.");
            string completeJournal = Path.Combine(workflow.RunFolder!, "journal.jsonl");
            operation.SelectedIndex = 1;
            interruptedJournal.Text = completeJournal;
            await LoadPlan();
            if (plan is not null || start.IsEnabled) throw new InvalidOperationException("A completed flash was armed for recovery.");
            operation.SelectedIndex = 0;
            await LoadPlan();
            driver.SelectedItem = driver.Items.OfType<J2534Driver>().Single(d => d.Experimental);
            if (start.IsEnabled || !readyLabel.Text.StartsWith("Ignition is on, engine is off", StringComparison.Ordinal))
                throw new InvalidOperationException("Experimental writing requires the standard setup confirmation.");
            ready.IsChecked = true;
            simulateInterruptedWrite();
            await RunSmokeOperation();
            if (workflow?.Stage != BenchFlashStage.RecoveryRequired || start.IsEnabled)
                throw new InvalidOperationException("Interrupted desktop run did not stop in the recovery-required state.");
            string stoppedJournal = Path.Combine(workflow.RunFolder!, "journal.jsonl");
            operation.SelectedIndex = 1;
            interruptedJournal.Text = stoppedJournal;
            await LoadPlan();
            if (recovery is null || start.IsEnabled) throw new InvalidOperationException("Recovery was not validated or was armed before confirmation.");
            if (start.IsEnabled || !readyLabel.Text.Contains("Same physical ECU", StringComparison.Ordinal))
                throw new InvalidOperationException("Experimental recovery requires same-ECU confirmation.");
            ready.IsChecked = true;
            await RunSmokeOperation();
            if (workflow?.Stage != BenchFlashStage.Complete || uiStateFailed || start.IsEnabled)
                throw new InvalidOperationException("Offline desktop recovery failed: " + status.Text);
            using var recovered = JsonDocument.Parse(File.ReadAllText(Path.Combine(workflow.RunFolder!, "verified-result.json")));
            if (recovered.RootElement.GetProperty("readbackPasses").GetInt32() != 2)
                throw new InvalidOperationException("Recovery did not require two readbacks.");
        }
        return new { passed = true, hardwareAccessed = false, planValidated = true, explicitSetupConfirmation = true, changedInputDisarms = true,
            rejectedInputRecovers = true, vlinkerSelectionVerified = true, offlineTransferVerified = offlineFactory is not null,
            offlineStockRecoveryVerified = offlineFactory is not null, mongooseRecoverySelectionVerified = driver.SelectedItem is J2534Driver { Experimental: true },
            progressAndLockingVerified = !uiStateFailed, target = plan?.FullCalibration };

        async Task RunSmokeOperation()
        {
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var watch = Stopwatch.StartNew();
            while (active && watch.Elapsed.TotalSeconds < 45) await Task.Delay(10);
            if (active) throw new TimeoutException("Offline desktop operation did not finish.");
        }
    }
}
