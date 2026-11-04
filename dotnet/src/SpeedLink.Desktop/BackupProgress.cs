using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private readonly Stopwatch readElapsed = new(), readTransferElapsed = new();
    private DispatcherTimer? readProgressTimer;
    private ReadProgress? currentReadProgress;
    private ReadProgressCapture? readProgressCapture;
    private long readProgressGeneration;
    private bool readProgressActive;

    private void ClearReadProgress()
    {
        readProgressActive = false;
        readProgressGeneration++; readProgressCapture = null;
        readProgressTimer?.Stop();
        readElapsed.Reset(); readTransferElapsed.Reset(); currentReadProgress = null;
        ReadProgressPanel.Visibility = Visibility.Collapsed;
        ReadBackupBar.Value = ProgressBar.Value = 0;
    }

    private void BeginReadProgress()
    {
        readProgressTimer?.Stop();
        readProgressActive = true;
        long generation = ++readProgressGeneration;
        readProgressCapture = new(new Progress<ReadProgress>(value =>
        {
            if (generation == readProgressGeneration) ShowReadProgress(value);
        }));
        currentReadProgress = null;
        readElapsed.Restart(); readTransferElapsed.Reset();
        ReadProgressPanel.Visibility = Visibility.Visible;
        ReadProgressStage.Text = "Preparing backup…";
        ReadProgressDetail.Text = "Checking ECU identity, engine stopped and read access.";
        ReadProgressPercent.Text = "0% overall";
        ReadBackupBar.Value = ProgressBar.Value = 0;
        UpdateReadTiming();
        readProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readProgressTimer.Tick += (_, _) => UpdateReadTiming();
        readProgressTimer.Start();
    }

    private void ShowReadProgress(ReadProgress value)
    {
        // Queued progress must not replace a stop, failure or completed result.
        if (!readProgressActive) return;
        currentReadProgress = value;
        if (value.Stage == BackupReadStage.Reading && !readTransferElapsed.IsRunning) readTransferElapsed.Start();
        if (value.Stage is BackupReadStage.Verifying or BackupReadStage.Complete) readTransferElapsed.Stop();
        ReadProgressStage.Text = stop?.IsCancellationRequested == true ? "Stopping after the current exchange…"
            : value.Stage == BackupReadStage.Complete ? "Verifying and saving backup…" : value.Message;
        ReadProgressDetail.Text = value.Stage switch
        {
            BackupReadStage.Preparing => "Checking ECU identity, engine stopped and read access.",
            BackupReadStage.Reading => $"{value.BytesInPass:N0} / {RomInspector.RomSize:N0} bytes in this pass · both passes are required",
            _ => "Both passes read · comparing the captures and saving the verified backup."
        };
        // Transfer completion is not backup completion. Only the successful return seals 100%.
        ReadBackupBar.Value = ProgressBar.Value = Math.Min(99, Math.Clamp(value.Fraction, 0, 1) * 100);
        ReadProgressPercent.Text = $"{Math.Floor(ReadBackupBar.Value):0}% overall";
        StatusText.Text = ReadProgressStage.Text;
        UpdateReadTiming();
    }

    private void UpdateReadTiming()
    {
        string elapsed = readElapsed.Elapsed.ToString(readElapsed.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        string remaining = "estimating time remaining…";
        if (stop?.IsCancellationRequested == true) remaining = "stopping…";
        else if (currentReadProgress?.Stage is BackupReadStage.Verifying or BackupReadStage.Complete) remaining = "finishing verification…";
        else if (currentReadProgress is { Stage: BackupReadStage.Reading, Fraction: > 0 and < 1 } progress && readTransferElapsed.Elapsed.TotalSeconds >= 2)
        {
            double seconds = readTransferElapsed.Elapsed.TotalSeconds * (1 - progress.Fraction) / progress.Fraction;
            remaining = seconds >= 60 ? $"about {Math.Ceiling(seconds / 60):0} min remaining" : $"about {Math.Max(1, Math.Ceiling(seconds)):0} s remaining";
        }
        ReadProgressTiming.Text = $"Elapsed {elapsed} · {remaining}";
    }

    private void FinishReadProgress(bool verified, bool stopped = false)
    {
        // The worker may have saved its final block before the dispatcher handled its callback.
        if (readProgressCapture?.Latest is { } final) ShowReadProgress(final);
        readProgressActive = false;
        readProgressTimer?.Stop(); readElapsed.Stop(); readTransferElapsed.Stop();
        string elapsed = readElapsed.Elapsed.ToString(readElapsed.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        ReadProgressTiming.Text = $"Elapsed {elapsed}";
        if (verified)
        {
            ReadProgressStage.Text = "Backup verified · two matching reads";
            ReadProgressDetail.Text = "Both complete reads match. The backup and verification records are saved.";
            ReadProgressPercent.Text = "100%";
            ReadBackupBar.Value = ProgressBar.Value = 100;
        }
        else
        {
            ReadProgressStage.Text = stopped ? "Backup stopped · not verified" : "Backup interrupted · not verified";
            ReadProgressDetail.Text = currentReadProgress is { Pass: > 0 } progress
                ? $"Pass {progress.Pass}: {progress.BytesInPass:N0} / {RomInspector.RomSize:N0} bytes read · no verified backup was created."
                : "No verified backup was created. Partial captures are retained.";
            ReadProgressTiming.Text += " · Reconnect before another read.";
        }
    }
}
