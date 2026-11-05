using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private const int FlashPage = 6;
    private FlashBackupLibrary flashBackups = null!;
    private FlashWorkspace? flashWorkspace;
    private string? handoffBackup;

    private FlashWorkspace ShowFlashWorkspace(FlashWorkspace? workspace = null)
    {
        if (flashWorkspace is null)
        {
            RememberFlashLocation();
            flashWorkspace = workspace ?? new(Path.Combine(vault, "flashing"), flashBackups);
            flashWorkspace.HardwareUseChanged += Controls;
            flashWorkspace.ConnectionRequested += () => Navigate(0);
            FlashTab.Content = flashWorkspace;
            flashWorkspace.SetConnectionUnavailable(busy || closing || exportingReport || transport is not null);
        }
        return flashWorkspace;
    }

    private void Gen1Flash_Click(object sender, RoutedEventArgs e) => Navigate(FlashPage);

    private void OpenBackupInFlash_Click(object sender, RoutedEventArgs e)
    {
        if (handoffBackup is null || !FlashBackupButton.IsEnabled) return;
        Navigate(FlashPage);
        var passThru = AdapterBox.SelectedIndex > 0;
        ShowFlashWorkspace().UseBackup(handoffBackup, passThru ? null : PortBox.Text.Trim(),
            passThru ? DriverBox.SelectedItem as J2534Driver : null);
    }

    private void OfferBackupForFlash(BackupResult result)
    {
        if (result.Inspection.ProfileValid && result.Inspection.ChecksumValid == true &&
            result.Inspection.Calibration is string id && BenchFlashWorkflow.IsNativeWriterVerified(id))
        {
            handoffBackup = result.Path;
            FlashBackupButton.Visibility = Visibility.Visible;
            RememberFlashLocation();
        }
        else
        {
            handoffBackup = null;
            FlashBackupButton.Visibility = Visibility.Collapsed;
        }
    }

    private void RememberFlashLocation()
    {
        try { flashBackups.Remember(vault); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Log("Could not share this data location across SpeedLink versions: " + error.Message); }
    }

    internal async Task<object> SmokeFlashWorkspace(string output, BenchFlashUiPeerState peer, string backup,
        RomInspector inspector, TuningCodeGuard guard)
    {
        // Register the old installation's operation folder, then let the new
        // workspace resolve the moved target and the interrupted run itself.
        flashBackups.Remember(Path.GetDirectoryName(backup)!);
        var wrongChassis = new EcuIdentity("simulator", "fixture", null, "L3M6EG000") { ChassisId = "GG3P999999" };
        var wrongCalibration = wrongChassis with { Calibration = "L38KEH000", ChassisId = "GG3P123456" };
        if (flashBackups.Candidates("simulator", wrongChassis, null).Any() ||
            flashBackups.Candidates("simulator", wrongCalibration, null).Any())
            throw new InvalidOperationException("A different chassis or calibration matched the saved backup.");
        var page = ShowFlashWorkspace(new(Path.Combine(vault, "flashing"), flashBackups, peer.Open, (inspector, guard)));
        Show();
        Navigation.Children.OfType<RadioButton>().Single(button => (string)button.Tag == "6").IsChecked = true;
        UpdateLayout();
        if (Pages.SelectedIndex != FlashPage || !page.IsVisible)
            throw new InvalidOperationException("The sidebar did not open Flash & Recover.");
        var report = await page.Smoke(backup, output, peer);
        Navigate(0);
        OfferBackupForFlash(new(backup, inspector.Inspect(File.ReadAllBytes(backup))));
        Controls();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (!FlashBackupButton.IsEnabled || !FlashBackupButton.IsVisible)
            throw new InvalidOperationException("Connection did not offer its verified Gen 1 backup.");
        FlashBackupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Pages.SelectedIndex != FlashPage || page.PreferredBackup != backup)
            throw new InvalidOperationException("Connection did not pass its backup into Flash & Recover.");
        return report;
    }
}
