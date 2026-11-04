using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<Dictionary<string, object?>> ExerciseReadProgressOffline(IReadTransport peer, string output, string scenario, bool unknownGen2 = false)
    {
        Show(); UpdateLayout();
        if (ReadProgressPanel.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Idle progress takes up space.");
        BeginReadProgress();
        stop = new();
        var oldProgress = readProgressCapture!;
        stop.Cancel();
        oldProgress.Report(new(.01, "last saved block") { Pass = 1, BytesInPass = 7936 });
        FinishReadProgress(false, stopped: true);
        if (currentReadProgress?.BytesInPass != 7936 || !ReadProgressDetail.Text.Contains(7936.ToString("N0"), StringComparison.Ordinal))
            throw new InvalidOperationException("A queued final saved block was omitted from stopped progress.");
        stop.Dispose(); stop = null;
        BeginReadProgress();
        oldProgress.Report(new(1, "old operation callback") { Pass = 2, BytesInPass = RomInspector.RomSize });
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (currentReadProgress is not null || ReadBackupBar.Value != 0)
            throw new InvalidOperationException("A prior operation changed the new read's progress.");
        ClearReadProgress();
        BeginReadProgress();
        ShowReadProgress(new(1, "Verifying and saving backup…") { Stage = BackupReadStage.Verifying, Pass = 2, BytesInPass = RomInspector.RomSize });
        if (ReadBackupBar.Value != 99 || ReadProgressPercent.Text.Contains("100", StringComparison.Ordinal)) throw new InvalidOperationException("Unverified transfer displayed 100%.");
        FinishReadProgress(false);
        string failed = ReadProgressStage.Text;
        ShowReadProgress(new(1, "late callback") { Stage = BackupReadStage.Complete });
        if (ReadProgressStage.Text != failed || ReadBackupBar.Value == 100) throw new InvalidOperationException("Late callback replaced a failure.");
        ClearReadProgress();
        if (ReadProgressPanel.Visibility != Visibility.Collapsed || ReadBackupBar.Value != 0 || ProgressBar.Value != 0)
            throw new InvalidOperationException("A new connection retained the previous ECU's backup result.");
        transport = peer; identity = ReadWorkflow.Identify(peer);
        ShowEcuIdentity();
        if (!IdentityText.Text.Contains(identity.VehicleIdentifierLabel + ": " + identity.VehicleIdentifier, StringComparison.Ordinal) ||
            !IdentityText.Text.Contains("Calibration: " + identity.Calibration, StringComparison.Ordinal))
            throw new InvalidOperationException("Connected ECU identifier/calibration is not displayed correctly.");
        Controls();
        if (unknownGen2)
        {
            if (!ReadFamilyBox.IsEnabled) throw new InvalidOperationException("Unknown ROM protocol cannot be selected.");
            ReadFamilyBox.SelectedIndex = 1;
        }
        bool firstPass = false, secondPass = false, locked = false, estimate = false, reset = false;
        void Capture(string name)
        {
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
            // Render a separate overview above the log so documentation contains no private backup paths.
            int height = (int)Math.Ceiling(ReadProgressPanel.TransformToAncestor(this).TransformBounds(new Rect(ReadProgressPanel.RenderSize)).Bottom + 12);
            var overview = new RenderTargetBitmap((int)ActualWidth, height, 96, 96, PixelFormats.Pbgra32); overview.Render(this);
            var overviewEncoder = new PngBitmapEncoder(); overviewEncoder.Frames.Add(BitmapFrame.Create(overview));
            using var overviewFile = File.Create(Path.Combine(output, name + "-progress.png")); overviewEncoder.Save(overviewFile);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            if (!readProgressActive) return;
            locked |= !ReadButton.IsEnabled && StopButton.IsEnabled;
            estimate |= ReadProgressTiming.Text.Contains(" min remaining", StringComparison.Ordinal) || ReadProgressTiming.Text.Contains(" s remaining", StringComparison.Ordinal);
            if (!firstPass && currentReadProgress is { Pass: 1, Fraction: >= .25 }) { firstPass = true; Capture("Pass1"); }
            if (!secondPass && currentReadProgress is { Pass: 2, Fraction: >= .75 }) { secondPass = true; Capture("Pass2"); }
            if (scenario == "cancel" && ReadBackupBar.Value >= 2) StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try
        {
            ReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            reset = readProgressActive && ReadBackupBar.Value == 0 && ReadProgressPercent.Text == "0% overall";
            locked |= !ReadButton.IsEnabled && StopButton.IsEnabled;
            await (activeWork ?? throw new InvalidOperationException("Read button did not start work.")).WaitAsync(TimeSpan.FromSeconds(45));
        }
        finally { timer.Stop(); }
        bool complete = scenario == "success";
        if (!reset || !locked || readProgressActive || readProgressTimer?.IsEnabled == true || StopButton.IsEnabled)
            throw new InvalidOperationException("Progress reset, locking or cleanup failed.");
        if (complete ? ReadBackupBar.Value != 100 || !firstPass || !secondPass || !estimate : ReadBackupBar.Value >= 100 || !ReadProgressStage.Text.Contains("not verified", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected read progress result: " + ReadProgressStage.Text + " · " + StatusText.Text);
        var entries = store.Scan(vault);
        if (entries.Count != 1 || entries[0].Complete != complete) throw new InvalidOperationException("Progress and durable backup disagree.");
        if (currentReadProgress is { Pass: > 0 } savedProgress)
        {
            var capture = new FileInfo(Path.Combine(Path.GetDirectoryName(entries[0].Path)!, $"pass{savedProgress.Pass}.read"));
            if (capture.Length != savedProgress.BytesInPass) throw new InvalidOperationException("Displayed bytes differ from the durable capture.");
        }
        string resultStage = ReadProgressStage.Text;
        double resultValue = ReadBackupBar.Value;
        ShowReadProgress(new(1, "late callback") { Stage = BackupReadStage.Complete });
        if (ReadProgressStage.Text != resultStage || ReadBackupBar.Value != resultValue) throw new InvalidOperationException("Late progress replaced final state.");
        Width = MinWidth; Height = MinHeight;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var bounds = ReadProgressPanel.TransformToAncestor(this).TransformBounds(new Rect(ReadProgressPanel.RenderSize));
        if (bounds.Right > ActualWidth || bounds.Bottom > ActualHeight - 26 || LogBox.ActualHeight < 50 || ReadBackupBar.ActualHeight < 10)
            throw new InvalidOperationException("Progress or log clipped at minimum window size.");
        Capture("Result");
        return new() { ["passed"] = true, ["scenario"] = scenario, ["hardwareAccessed"] = false, ["actualReadButton"] = true,
            ["vehicleIdentifierLabel"] = identity.VehicleIdentifierLabel, ["identityDisplayVerified"] = true,
            ["firstPassObserved"] = firstPass, ["secondPassObserved"] = secondPass, ["estimatedTimeObserved"] = estimate,
            ["progressReset"] = reset, ["newConnectionClearsResult"] = true, ["controlsLocked"] = locked, ["unverifiedTransferCapped"] = true, ["lateCallbacksIgnored"] = true,
            ["minimumWindowVerified"] = true, ["progress"] = resultValue, ["stage"] = resultStage, ["backupComplete"] = entries[0].Complete };
    }
}
