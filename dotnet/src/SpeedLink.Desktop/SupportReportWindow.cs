using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed class SupportReportWindow : Window
{
    private sealed record Session(string Folder, string Label) { public override string ToString() => Label; }
    private readonly ComboBox chooser = new() { Margin = new(0, 8, 0, 12), MinHeight = 30 };
    private readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(12) };
    private readonly Button save = new() { Content = "Save support ZIP…", Margin = new(8, 0, 0, 0), IsDefault = true };
    internal SupportReportWindow(string root)
    {
        Title = "SpeedLink · Export support report"; Width = 760; Height = 610; MinWidth = 560; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiTheme.Panel; Foreground = UiTheme.Text;
        var panel = new DockPanel { Margin = new(18) }; Content = panel;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Choose the session with the problem", FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(chooser);
        header.Children.Add(new TextBlock { Text = "Includes adapter/driver setup, requests, timing, errors and factory security access. VIN/chassis IDs, adapter serial and personal paths are redacted. ROM bytes and recordings are kept in your backups.", TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, Margin = new(0, 0, 0, 12) });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var cancel = new Button { Content = "Close", IsCancel = true }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        var issue = new Button { Content = "Open problem form", ToolTip = "Opens GitHub in your browser. Attach the saved ZIP yourself." };
        issue.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml") { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot open browser"); }
        };
        footer.Children.Add(issue); footer.Children.Add(save);
        panel.Children.Add(preview);
        var sessions = new List<Session>();
        foreach (string folder in SupportReport.Sessions(root).Take(10))
        {
            try
            {
                JsonObject data = SupportReport.Metadata(folder);
                sessions.Add(new(folder, $"{data["startedUtc"]} · {data["status"]} · {data["appVersion"]}"));
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        chooser.ItemsSource = sessions;
        chooser.SelectionChanged += (_, _) => UpdatePreview();
        chooser.SelectedIndex = sessions.Count > 0 ? 0 : -1;
        UpdatePreview(); save.Click += Save_Click;
    }
    private void UpdatePreview()
    {
        save.IsEnabled = chooser.SelectedItem is Session;
        try { preview.Text = chooser.SelectedItem is Session session ? SupportReport.Summary(SupportReport.Metadata(session.Folder)) : "No saved diagnostic sessions. Connect an adapter to start a capture."; }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException) { preview.Text = "This session cannot be read: " + new DiagnosticRedactor().Text(error.Message); save.IsEnabled = false; }
    }
    internal async Task ExportTo(string destination)
    {
        var session = chooser.SelectedItem as Session ?? throw new InvalidOperationException("Choose a diagnostic session.");
        chooser.IsEnabled = false; save.IsEnabled = false;
        try { await Task.Run(() => SupportReport.Export(session.Folder, destination)); preview.Text = "Support report saved. Attach this ZIP to your support request.\n\n" + SupportReport.Summary(SupportReport.Metadata(session.Folder)); }
        finally { chooser.IsEnabled = true; save.IsEnabled = true; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save a new support report", Filter = "Support report|*.zip", DefaultExt = ".zip", FileName = "SpeedLink-support-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        try { await ExportTo(dialog.FileName); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot save support report", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
