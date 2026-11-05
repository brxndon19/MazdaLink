using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class App
{
    internal static CrashReports Reports(string? root = null) => new(
        root ?? Path.Combine(AppContext.BaseDirectory, "data", "crashes"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedLink", "crashes"));
    private int handlingFatal;
    private string? crashTestRoot;
    private bool crashProbe;
    private MainWindow? crashWindow;
    private void InstallCrashReporting()
    {
        DispatcherUnhandledException += OnDispatcherFailure;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CaptureFailure(args.ExceptionObject as Exception ?? new Exception("Unknown runtime failure"), "Runtime", true);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // This notification is nonfatal. Log it without claiming that an arbitrary failed operation recovered.
            CaptureFailure(args.Exception, "Unobserved background task", false); args.SetObserved();
        };
    }
    private void CaptureFailure(Exception error, string origin, bool fatal)
    {
        if (fatal && System.Threading.Interlocked.Exchange(ref handlingFatal, 1) != 0) return;
        try
        {
            var window = crashWindow;
            Reports(crashTestRoot).Capture(error, SpeedLink.Desktop.MainWindow.Version, origin, fatal, window?.CrashActivity ?? "Starting SpeedLink", window?.CrashRedactor);
            if (fatal) window?.CancelForCrash();
        }
        catch (Exception) { /* A last-resort recorder must never replace the original failure. */ }
    }
    private void OnDispatcherFailure(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        CaptureFailure(args.Exception, "User interface", true);
        args.Handled = true;
        // Do not run window-closing save handlers or continue in an unknown state. Existing drafts stay intact.
        try { if (!crashProbe) MessageBox.Show("SpeedLink encountered an unexpected error and must close. Restart SpeedLink to review a local crash report and reopen your last saved ROM copy.", "SpeedLink", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Environment.Exit(70); }
    }
    private void OfferCrashReports(Window owner)
    {
        var reports = Reports();
        if (reports.List(true).Count > 0) new CrashReportWindow(reports, true) { Owner = owner }.ShowDialog();
    }
}

public partial class MainWindow
{
    // Read only fields from fatal/background callbacks; never traverse WPF objects on a worker thread.
    internal string CrashActivity => busy ? (loggingActive ? "Logging" : "Adapter operation") : "Idle or editing";
    internal DiagnosticRedactor? CrashRedactor => diagnosticSession?.Redactor;
    internal void CancelForCrash() { try { stop?.Cancel(); } catch (ObjectDisposedException) { } }
    private void CrashReports_Click(object sender, RoutedEventArgs args)
    {
        if (busy || closing) return;
        new CrashReportWindow(App.Reports()) { Owner = this }.ShowDialog();
    }
}

internal sealed class CrashReportWindow : Window
{
    private readonly CrashReports reports;
    private readonly ComboBox chooser = new() { Margin = new(0, 12, 0, 12) };
    private readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(10) };
    internal CrashReportWindow(CrashReports reports, bool afterRestart = false)
    {
        UiTheme.Ensure();
        this.reports = reports;
        Title = afterRestart ? "SpeedLink · Report from the previous session" : "SpeedLink · Crash reports";
        Width = 740; Height = 510; MinWidth = 540; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = UiTheme.Panel; Foreground = UiTheme.Text;
        var panel = new DockPanel { Margin = new(18) }; Content = panel;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = afterRestart ? "SpeedLink recorded an unexpected error" : "Saved local crash reports", FontSize = 21, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Review and save a support ZIP to attach to a problem report. Nothing is sent automatically. To resume editing, reopen your last saved ROM copy or original backup. Unsaved changes can be lost.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) });
        header.Children.Add(chooser);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var save = new Button { Content = "Save crash ZIP…" }; footer.Children.Add(save);
        var close = new Button { Content = "Close", IsCancel = true }; footer.Children.Add(close); close.Click += (_, _) => Close();
        panel.Children.Add(preview);
        var displayedPending = reports.List(true);
        chooser.ItemsSource = reports.List();
        chooser.SelectionChanged += (_, _) =>
        {
            if (chooser.SelectedItem is not string path) return;
            try { var report = reports.Read(path); preview.Text = $"SpeedLink {report.Version} · {report.TimeUtc:u}\n{report.Origin} · {report.Activity}\n\n{report.ExceptionType}\n{report.Message}\n\n{report.Stack}"; }
            catch (Exception error) { preview.Text = "Cannot read this report: " + new DiagnosticRedactor().Text(error.Message); }
        };
        chooser.ItemTemplate = new DataTemplate();
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new CrashLabel(reports) }); chooser.ItemTemplate.VisualTree = label;
        chooser.SelectedIndex = chooser.Items.Count > 0 ? 0 : -1; save.IsEnabled = chooser.Items.Count > 0;
        if (chooser.Items.Count == 0) preview.Text = "No saved crash reports.";
        save.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Crash support ZIP|*.zip", FileName = "SpeedLink-crash.zip", DefaultExt = ".zip" };
            if (chooser.SelectedItem is string path && dialog.ShowDialog(this) == true)
                try { ExportTo(path, dialog.FileName); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot save crash report"); }
        };
        Closed += (_, _) =>
        {
            foreach (string path in displayedPending) try { reports.Acknowledge(path); } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        };
    }
    internal void ExportTo(string path, string destination) => reports.Export(path, destination);
    private sealed class CrashLabel(CrashReports reports) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        { try { var report = reports.Read((string)value); return $"{report.TimeUtc:u} · {report.Origin} · {report.Version}"; } catch (Exception) { return "Unreadable report"; } }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
