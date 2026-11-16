using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

/// <summary>Read-only Mazda 2 DE ROM preflight. No ECU transport or write actions are exposed.</summary>
internal sealed class Mazda2FlashWorkspace : UserControl
{
    private readonly TextBox original = new() { MinWidth = 360 };
    private readonly TextBox modified = new() { MinWidth = 360 };
    private readonly TextBlock report = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    private readonly TextBlock recovery = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button run = new() { Content = "Validate & run dry-run", MinWidth = 190 };
    private string? preferredOriginal;

    internal Mazda2FlashWorkspace()
    {
        Background = UiTheme.Panel;
        Foreground = UiTheme.Text;
        var panel = new StackPanel { Margin = new(22), MaxWidth = 1050 };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Mazda 2 DE · Flash & Recovery", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "ZY-VE · Denso SH7058 · ZY87EC000 / ZYV5050", Foreground = UiTheme.Muted, Margin = new(0, 5, 0, 12) });
        panel.Children.Add(new TextBlock
        {
            Text = "DRY-RUN ONLY — live erase and programming are disabled. This preflight checks the original backup, ROM identity, all 19 checksums, and whether edits are confined to the calibration region.",
            TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Amber, Margin = new(0, 0, 0, 14)
        });
        AddPath(panel, "Original ECU backup", original, "Choose the unmodified 1 MiB ROM read from your ECU.");
        AddPath(panel, "Modified ROM", modified, "Choose the edited 1 MiB ROM you intend to flash.");
        run.Margin = new(0, 12, 0, 4);
        run.Click += (_, _) => RunPreflight();
        panel.Children.Add(run);
        report.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        panel.Children.Add(report);
        panel.Children.Add(new TextBlock { Text = "Factory recovery research", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 22, 0, 4) });
        recovery.Text = "RESEARCH ONLY — The SH7058 silicon documents a hardware-selected boot mode using MD/FWE mode pins and its SCI serial interface. This is distinct from the ECU's normal CAN/OBD read session. We have not yet verified the Mazda 2 ECU board pinout, watchdog requirements, connector wiring, or exact boot command sequence. Do not enter this mode experimentally: the full user MAT and user boot MAT are erased after the inquiry/selection phase, before programming starts. Recovery must remain disabled until the exact ECU-level procedure and a successful bench recovery are independently verified.";
        panel.Children.Add(recovery);
        if (preferredOriginal is not null) original.Text = preferredOriginal;
    }

    internal void UseOriginalBackup(string path)
    {
        preferredOriginal = path;
        original.Text = path;
    }

    private void AddPath(Panel parent, string label, TextBox box, string hint)
    {
        parent.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 4) });
        var row = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "ROM images|*.rom;*.bin|All files|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) box.Text = dialog.FileName;
        };
        row.Children.Add(browse);
        row.Children.Add(box);
        parent.Children.Add(row);
        parent.Children.Add(new TextBlock { Text = hint, Foreground = UiTheme.Muted, Margin = new(0, 3, 0, 4) });
    }

    private void RunPreflight()
    {
        report.Text = "";
        try
        {
            var result = Mazda2FlashPreflight.Analyze(original.Text.Trim(), modified.Text.Trim());
            report.Text =
                "DRY-RUN PASSED — no ECU communication was attempted.\n\n" +
                $"Software ID: {result.SoftwareId}\n" +
                $"Original SHA-256: {result.OriginalSha256}\n" +
                $"Modified SHA-256: {result.ModifiedSha256}\n\n" +
                $"Calibration bytes changed: {result.ChangedCalibrationBytes:N0}\n" +
                $"Engine program bytes changed: {result.ChangedProgramBytes:N0}\n" +
                $"Boot block bytes changed: {result.ChangedBootBytes:N0}\n" +
                $"Other bytes changed: {result.ChangedOtherBytes:N0}\n\n" +
                $"Original checksum ranges: {result.OriginalChecksums.Count(x => x.Valid)}/19 PASS\n" +
                $"Modified checksum ranges: {result.ModifiedChecksums.Count(x => x.Valid)}/19 PASS\n\n" +
                "Proposed scope: calibration-only changes in 0x0C0000–0x0F7FFF.\n" +
                "Programming protocol: not established. No write commands were generated or sent.\n" +
                "Original image authenticity: not independently established; compare its hash with your trusted backup records.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            report.Text = "DRY-RUN BLOCKED\n\n" + error.Message + "\n\nNo ECU communication was attempted.";
        }
    }
}
