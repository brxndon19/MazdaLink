using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpeedLink.Core;
using SpeedLink.Hardware;
using SpeedLink.J2534;

namespace SpeedLink.Desktop;

public partial class MainWindow : Window
{
    public static readonly string Version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?.InformationalVersion.Split('+')[0] ?? "unknown";
    private void Editor_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private readonly string vault;
    private readonly RomInspector inspector = new();
    private readonly BackupStore store = new();
    private IReadTransport? transport;
    private FtdiSerialConnection? connection;
    private EcuIdentity? identity;
    private byte[]? image;
    private string? imagePath;
    private RomInspection? inspection;
    private bool busy, closing;
    private CancellationTokenSource? stop;
    private Task? activeWork;
    private readonly Dictionary<J2534AdapterKind, string> selectedDrivers = [];
    private J2534AdapterKind SelectedDriverKind => AdapterBox.SelectedIndex == 2 ? J2534AdapterKind.MongoosePlusIsoCan3 : J2534AdapterKind.Tactrix;
    public MainWindow() : this(null) { }
    internal MainWindow(string? testVault, RomInspector? editorInspector = null, TuningCodeGuard? editorGuard = null)
    {
        vault = testVault ?? Path.Combine(AppContext.BaseDirectory, "data");
        definitionLibrary = new(Path.Combine(vault, "definitions")); definitionSnapshot = definitionLibrary.Load();
        UiTheme.Ensure(); InitializeComponent();
        if (editorInspector is not null && editorGuard is not null)
        {
            EditorPane = new(editorInspector, editorGuard); ((TabItem)Pages.Items[1]).Content = EditorPane;
        }
        EditorPane.Definitions = definitionSnapshot.Editors;
        ConfigureApplicationMenus();
        ConfigureWorkspaceLayout();
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        Navigate(0);
        Closed += (_, _) => { RecordingsPane.CancelLoading(); readProgressTimer?.Stop(); };
        Title = $"SpeedLink · Alpha {Version}";
        VersionLabel.Text = $"ALPHA {Version}";
        BaudBox.ItemsSource = new[] { 115200, 38400, 57600, 230400, 500000, 1000000, 2000000 };
        BaudBox.SelectedItem = 115200;
        AdapterBox.ItemsSource = new[] { "Vgate vLinker FS", "Tactrix Openport 2.0", "Mongoose-Plus ISO/CAN3 (experimental)" };
        AdapterBox.SelectedIndex = 0;
        ReadFamilyBox.ItemsSource = new[] { "Gen 1 read protocol", "Gen 2 read protocol (experimental)" };
        ReadFamilyBox.SelectedIndex = 0;
        InitializeLogger();
        RefreshPorts();
        RefreshHistory();
        Controls();
        if (definitionSnapshot.Entries.Any(e => e.Problem is not null)) StatusText.Text = "Some imported definitions were rejected · open Definitions for details";
    }
    private void Log(string message)
    {
        diagnosticSession?.Record("ui.message", new { message });
        LogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
    private void Controls()
    {
        bool idle = !busy && !closing && !exportingReport;
        DefinitionsButton.IsEnabled = idle && transport is null;
        CrashReportsButton.IsEnabled = idle;
        SessionText.Text = transport is null ? "Offline" : identity?.Calibration ?? "Adapter connected";
        ConnectButton.IsEnabled = idle && transport is null;
        DisconnectButton.IsEnabled = idle && transport is not null;
        ReadButton.IsEnabled = idle && transport is not null && identity is not null && !transport.ReadAttempted && !loggerReadReconnectRequired;
        ReadButton.ToolTip = loggerReadReconnectRequired ? "Disconnect and reconnect before reading a ROM after logging." : "Read and verify two matching copies of the ECU ROM.";
        var readProfiles = RomInspector.KnownProfiles.Where(p => RomInspector.MatchesCalibrationId(p, identity?.Calibration)).ToArray();
        if (readProfiles.Length == 1) ReadFamilyBox.SelectedIndex = readProfiles[0].Family == "Gen2" ? 1 : 0;
        ReadFamilyBox.IsEnabled = idle && readProfiles.Length == 0;
        StopButton.IsEnabled = busy;
        OpenRomButton.IsEnabled = idle;
        CorrectButton.IsEnabled = idle && inspection?.CanCorrectChecksum == true;
        OpenBackupButton.IsEnabled = idle;
        PortBox.IsEnabled = idle && transport is null;
        BaudBox.IsEnabled = PortBox.IsEnabled;
        RefreshPortsButton.IsEnabled = PortBox.IsEnabled;
        RefreshHistoryButton.IsEnabled = idle;
        AdapterBox.IsEnabled = idle && transport is null;
        DriverBox.IsEnabled = AdapterBox.IsEnabled;
        LoggerControls(idle);
        DiagnosticControls(idle);
    }
    private Task Run(Func<Task> action)
    {
        activeWork = RunCore(action);
        return activeWork;
    }
    private async Task RunCore(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        stop?.Dispose();
        stop = new();
        Controls();
        try
        {
            await action();
        }
        catch (OperationCanceledException) { StatusText.Text = "Stopped · partial files and journal retained"; Log(StatusText.Text); RefreshHistory(); }
        catch (Exception error) { diagnosticSession?.Error("operation.error", error); StatusText.Text = "Stopped · " + error.Message; Log(error.Message); RefreshHistory(); }
        finally
        {
            busy = false;
            Controls();
            if (closing)
                await FinishClose();
        }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        bool passThruAdapter = AdapterBox.SelectedIndex > 0;
        var name = PortBox.Text.Trim();
        var baud = (int)BaudBox.SelectedItem;
        var driver = DriverBox.SelectedItem as J2534Driver;
        if (passThruAdapter && (driver is null || driver.Kind != SelectedDriverKind))
        {
            MessageBox.Show(this, SelectedDriverKind == J2534AdapterKind.MongoosePlusIsoCan3
                ? "Install the manufacturer's Mongoose-Plus ISO/CAN3 driver with 32-bit J2534-1 04.04 / ISO15765 support, then refresh devices."
                : "Install the Tactrix Openport drivers, then refresh devices.", "J2534 driver needed");
            return;
        }
        if (!passThruAdapter && name.Length == 0)
        {
            MessageBox.Show(this, "Choose the COM port assigned to your adapter.", "Select a port");
            return;
        }
        StatusText.Text = "Connecting and identifying…";
        await Run(async () =>
        {
            ClearReadProgress();
            await BeginDiagnostics();
            diagnosticSession?.Record("connection.opening", new { adapter = AdapterBox.Text, port = passThruAdapter ? null : name, baud = passThruAdapter ? 0 : baud });
            var connected = await Task.Run(() =>
            {
                FtdiSerialConnection? serial = null;
                IReadTransport link;
                if (passThruAdapter)
                {
                    var passThru = new J2534ReadTransport(new NativeJ2534Channel(driver!, diagnosticSession), diagnostics: diagnosticSession);
                    link = passThru;
                    try { passThru.Initialize(); }
                    catch { passThru.Dispose(); throw; }
                }
                else
                {
                    serial = new FtdiSerialConnection(name, baud);
                    diagnosticSession?.Record("serial.opened", new { backend = FtdiSerialConnection.Backend, originalLatencyMs = serial.OriginalLatency, latencyMs = 1, baud });
                    var elm = new VLinkerReadTransport(serial, diagnostics: diagnosticSession);
                    link = elm;
                    try { elm.Initialize(); }
                    catch { elm.Dispose(); throw; }
                }
                try
                {
                    if (diagnosticSession is not null) link = new DiagnosticTransport(link, diagnosticSession);
                    return (Link: link, Connection: serial, Identity: ReadWorkflow.Identify(link));
                }
                catch { link.Dispose(); throw; }
            });
            transport = connected.Link;
            connection = connected.Connection;
            identity = connected.Identity;
            diagnosticSession?.Record("connection.ready", new { identity.Adapter, identity.Calibration, identity.VinStatus, identity.CalibrationStatus });
            LoggerIdentityChanged();
            ShowEcuIdentity();
            StatusText.Text = "Connected · identification recorded";
            Log(IdentityText.Text);
        });
    }
    private void ShowEcuIdentity()
    {
        if (identity is not null)
            IdentityText.Text = $"{identity.Adapter}\n{identity.VehicleIdentifierLabel}: {identity.VehicleIdentifier ?? "unavailable"}    Calibration: {identity.Calibration ?? "unavailable"}";
    }
    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await Run(Disconnect);
    private async Task Disconnect()
    {
        var link = transport;
        var serial = connection;
        transport = null;
        connection = null;
        identity = null;
        loggerReadReconnectRequired = false;
        if (link is not null)
        {
            await Task.Run(link.Dispose);
            var device = link is DiagnosticTransport traced ? traced.Inner : link;
            if (device is VLinkerReadTransport elm)
            {
                BaudBox.SelectedItem = elm.Baud;
                if (elm.CloseWarning is not null)
                    Log("Adapter baud restore: " + elm.CloseWarning);
            }
            if (device is J2534ReadTransport passThru && passThru.CloseWarning is not null)
                Log("J2534 cleanup: " + passThru.CloseWarning);
            if (serial?.CleanupWarning is not null)
                Log("Adapter driver cleanup: " + serial.CleanupWarning);
        }
        IdentityText.Text = "Disconnected. No ECU has been identified.";
        StatusText.Text = "Disconnected";
        Log("Disconnected.");
    }
    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (transport is null || identity is null || loggerReadReconnectRequired)
            return;
        var link = transport;
        var expected = identity;
        var family = ReadFamilyBox.SelectedIndex == 1 ? ReadFamily.Gen2 : ReadFamily.Gen1;
        await Run(async () =>
        {
            BeginReadProgress();
            BackupResult result;
            try
            {
                var progress = new Progress<ReadProgress>(ShowReadProgress);
                result = await Task.Run(() => new ReadWorkflow(store, inspector).ReadBackup(link, expected, vault, progress, stop!.Token, family));
                FinishReadProgress(true);
            }
            catch (OperationCanceledException) { FinishReadProgress(false, stopped: true); throw; }
            catch { FinishReadProgress(false); throw; }
            BackupText.Text = "Verified backup: " + result.Path;
            Log("Verified backup saved: " + result.Path);
            if (!result.Inspection.ProfileRecognized)
                Log("Unknown ROM saved. The two reads match; checksum validation is unavailable for this layout.");
            else if (!result.Inspection.ProfileValid)
                Log("ROM saved. The two reads match, but one or more known-ROM checks did not pass.");
            StatusText.Text = "Backup verified · two matching reads";
            RefreshHistory();
        });
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        stop?.Cancel();
        if (readProgressActive) ReadProgressStage.Text = "Stopping after the current exchange…";
        StatusText.Text = "Stopping after the current exchange…";
    }
    private void RefreshPorts()
    {
        var found = SerialConnection.GetPorts();
        PortBox.ItemsSource = found;
        if (PortBox.Text.Length == 0 && found.Length > 0)
            PortBox.Text = found[0];
        RefreshDrivers();
    }
    private void RefreshDrivers()
    {
        if (DriverBox.SelectedItem is J2534Driver selected)
            selectedDrivers[selected.Kind] = selected.LibraryPath;
        var kind = SelectedDriverKind;
        selectedDrivers.TryGetValue(kind, out var selectedDriver);
        var drivers = J2534Drivers.Discover(kind);
        DriverBox.ItemsSource = drivers;
        if (drivers.Count > 0)
            DriverBox.SelectedItem = drivers.FirstOrDefault(item => item.LibraryPath.Equals(selectedDriver, StringComparison.OrdinalIgnoreCase))
                ?? drivers.FirstOrDefault(item => Path.GetFileName(item.LibraryPath).Equals("op20pt32.dll", StringComparison.OrdinalIgnoreCase)) ?? drivers[0];
    }
    private void Adapter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SerialPanel is null || TactrixPanel is null)
            return;
        bool passThruAdapter = AdapterBox.SelectedIndex > 0;
        bool experimental = AdapterBox.SelectedIndex == 2;
        SerialPanel.Visibility = passThruAdapter ? Visibility.Collapsed : Visibility.Visible;
        TactrixPanel.Visibility = passThruAdapter ? Visibility.Visible : Visibility.Collapsed;
        AdapterNote.Visibility = experimental ? Visibility.Visible : Visibility.Collapsed;
        RefreshDrivers();
        StatusText.Text = experimental ? "Experimental · select the installed Mongoose-Plus ISO/CAN3 driver" : passThruAdapter ? "Ready · select the installed Tactrix driver" : "Ready · select your adapter COM port";
    }
    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();
    private void RefreshHistory()
    {
        var entries = store.Scan(vault);
        HistoryGrid.ItemsSource = entries.Select(row => new { row.Mode, row.Operation, Status = row.Complete ? "Complete" : row.Valid ? "Interrupted / stopped" : "Journal damaged", Folder = Path.GetDirectoryName(row.Path) }).ToArray();
        HistoryNote.Text = $"{entries.Count} saved operation(s). Storage: {vault}";
    }
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(vault);
        Process.Start(new ProcessStartInfo(vault) { UseShellExecute = true });
    }
    private void LoadImage(string path)
    {
        if (new FileInfo(path).Length != RomInspector.RomSize)
            throw new InvalidDataException("Select a complete 1 MiB image.");
        var data = File.ReadAllBytes(path);
        var info = inspector.Inspect(data);
        image = data;
        imagePath = path;
        inspection = info;
        static string Show(bool? value) => value is null ? "Not available" : value.Value ? "PASS" : "FAIL";
        var text = new StringBuilder($"File: {path}\n\n{(info.ProfileRecognized ? "Known ROM: " + info.Calibration : "Unknown ROM — no known checksum layout")}\nSize: {info.Size:N0} bytes\n\nSHA-256:\n{info.Sha256}\n\n");
        if (info.ProfileRecognized)
        {
            text.AppendLine($"Software identity: {Show(info.IdentityValid)}\nRecognized boot code: {Show(info.BootRecognized)}\nChecksum layout: {Show(info.ChecksumLayoutRecognized)}");
            if (info.ChecksumLayoutRecognized)
            {
                text.AppendLine($"Checksum: {info.Checksum} / expected {info.ExpectedChecksum} [{Show(info.ChecksumValid)}]");
                if (info.IntegrityPolicy == "native_code_validation")
                    text.AppendLine("Code sum: 0x004000–0x0BFFFF, excluding 0x01C000–0x01FFFF. Four application markers are also checked.\nCalibration edits lie outside this sum. Invalid code is rejected; automatic repair is unavailable. Additional calibration integrity is not established.\n");
                else text.AppendLine($"Checksum region: 0x{info.ChecksumStart:X6}–0x0FFFFF\nCorrection word: 0x{info.CorrectionAddress:X6}\n");
            }
            else
                text.AppendLine("Checksum validation: unavailable for this layout.\n");
        }
        else
            text.AppendLine("Checksum validation: unavailable for this layout. A definition is not required to read or save the ROM.\n");
        text.AppendLine("Vehicle match and factory provenance: unverified. Checksum correction saves a separate file.");
        RomText.Text = text.ToString();
        Controls();
    }
    private void OpenRom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open full 1 MiB ROM", Filter = "ROM images|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(this) == true)
            TryFile(() => LoadImage(dialog.FileName));
    }
    private void Correct_Click(object sender, RoutedEventArgs e)
    {
        if (image is null || imagePath is null || inspection?.CanCorrectChecksum != true)
            return;
        TryFile(() =>
        {
            var corrected = inspector.CorrectedCopy(image);
            var dialog = new SaveFileDialog { Title = "Save a new checksum-corrected copy", FileName = Path.GetFileNameWithoutExtension(imagePath) + "_checksum.bin", DefaultExt = ".bin", Filter = "ROM images|*.bin" };
            if (dialog.ShowDialog(this) == true)
            {
                DurableStorage.NewFile(store.FileSystem, dialog.FileName, corrected);
                Log("Saved corrected copy: " + dialog.FileName);
            }
        });
    }
    private void OpenBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select rom.bin from a verified backup", Filter = "Backup ROM|rom.bin" };
        if (dialog.ShowDialog(this) == true)
            TryFile(() => LoadVerifiedBackup(dialog.FileName));
    }
    private void LoadVerifiedBackup(string path)
    {
        store.Load(path);
        LoadImage(path);
        BackupText.Text = "Verified backup: " + path;
    }
    private void TryFile(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Cannot open or save file", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        Resource("SpeedLink.About.txt").Replace("{version}", Version).Replace("{runtime}", Environment.Version.ToString()), "About SpeedLink");
    private Window HelpWindow(string document = "Getting started")
    {
        var documents = new Dictionary<string, string> { ["Getting started"] = "SpeedLink.Help.md", ["Adapters / testing"] = "SpeedLink.AdapterHelp.md", ["Report a problem"] = "SpeedLink.SupportHelp.md", ["Logger"] = "SpeedLink.LoggerHelp.md", ["Editor"] = "SpeedLink.EditorHelp.md", ["Gen 2 (experimental)"] = "SpeedLink.Gen2Help.md", ["GNU GPL license"] = "SpeedLink.LICENSE", ["Known-ROM checks"] = "SpeedLink.Profiles.md" };
        foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith("SpeedLink.Notices.", StringComparison.Ordinal)))
            documents["Notices / " + name[18..]] = name;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var chooser = new ComboBox { ItemsSource = documents.Keys, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(chooser, Dock.Top);
        panel.Children.Add(chooser);
        var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
        panel.Children.Add(text);
        chooser.SelectionChanged += (_, _) => text.Text = Resource(documents[(string)chooser.SelectedItem]);
        chooser.SelectedItem = documents.ContainsKey(document) ? document : "Getting started";
        return new Window { Owner = this, Title = "SpeedLink — Help & licenses", Width = 860, Height = 650, Content = panel };
    }
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow().Show();
    private bool closeApproved;
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!closeApproved && !EditorPane.MayClose()) { e.Cancel = true; return; }
        if (!closeApproved) SaveLoggerSettings();
        closeApproved = true;
        EditorPane.PrepareHostClose();
        if (transport is null && !busy && diagnosticSession is null)
            return;
        e.Cancel = true;
        closing = true;
        if (busy)
        {
            stop?.Cancel();
            StatusText.Text = "Stopping before closing…";
        }
        else
            await FinishClose();
    }
    private async Task FinishClose()
    {
        try
        {
            await Disconnect();
        }
        catch (Exception error) { Log(error.Message); }
        finally
        {
            if (diagnosticSession is not null) { await diagnosticSession.DisposeAsync(); diagnosticSession = null; }
            closing = false; stop?.Dispose(); Close();
        }
    }
    public void RunSmoke(string[] args)
    {
        Show();
        UpdateLayout();
        if (new System.Windows.Automation.Peers.TabControlAutomationPeer(Pages).GetChildren()?.Count != 6) throw new InvalidOperationException("Workspace automation containers missing.");
        if (Pages.Items.Count != 6 || transport is not null || image is not null || !ConnectButton.IsEnabled || ReadButton.IsEnabled || CorrectButton.IsEnabled)
            throw new InvalidOperationException("Unexpected startup state.");
        if (AdapterBox.Items.Count != 3 || AdapterBox.SelectedIndex != 0 || typeof(ReadWorkflow).Assembly.GetType("SpeedLink.Core.Simulator") is not null)
            throw new InvalidOperationException("Unexpected connection modes.");
        AdapterBox.SelectedIndex = 1;
        UpdateLayout();
        if (SerialPanel.Visibility != Visibility.Collapsed || TactrixPanel.Visibility != Visibility.Visible || ReadButton.IsEnabled)
            throw new InvalidOperationException("Unexpected Tactrix selection state.");
        if (DriverBox.Items.Count > 1)
        {
            var originalDriver = DriverBox.SelectedItem as J2534Driver;
            DriverBox.SelectedIndex = 0;
            var selected = ((J2534Driver)DriverBox.SelectedItem).LibraryPath;
            RefreshPorts();
            if ((DriverBox.SelectedItem as J2534Driver)?.LibraryPath != selected)
                throw new InvalidOperationException("Driver refresh lost the selected registered entry.");
            DriverBox.SelectedItem = DriverBox.Items.Cast<J2534Driver>().First(item => item.LibraryPath == originalDriver!.LibraryPath);
        }
        AdapterBox.SelectedIndex = 2; UpdateLayout();
        if (SerialPanel.Visibility != Visibility.Collapsed || TactrixPanel.Visibility != Visibility.Visible || AdapterNote.Visibility != Visibility.Visible ||
            !StatusText.Text.Contains("Experimental", StringComparison.Ordinal) || ReadButton.IsEnabled ||
            DriverBox.Items.Cast<J2534Driver>().Any(d => d.Kind != J2534AdapterKind.MongoosePlusIsoCan3 || !d.Experimental))
            throw new InvalidOperationException("Unexpected experimental Mongoose selection state.");
        RefreshPorts();
        AdapterBox.SelectedIndex = 1; UpdateLayout();
        if (AdapterNote.Visibility != Visibility.Collapsed || DriverBox.Items.Cast<J2534Driver>().Any(d => d.Kind != J2534AdapterKind.Tactrix))
            throw new InvalidOperationException("Experimental driver selection leaked into the Tactrix path.");
        AdapterBox.SelectedIndex = 0;
        VerifyLoggerSmoke(args);
        if (!Resource("SpeedLink.About.txt").Contains("{version}", StringComparison.Ordinal))
            throw new InvalidDataException("About resource missing.");
        if (!Resource("SpeedLink.AdapterHelp.md").Contains("Mongoose-Plus ISO/CAN3 (experimental)", StringComparison.Ordinal))
            throw new InvalidDataException("Adapter testing guide missing.");
        var help = HelpWindow();
        help.Show();
        help.UpdateLayout();
        help.Close();
        int open = Array.IndexOf(args, "--open-backup");
        if (open >= 0 && open + 1 < args.Length)
        {
            LoadVerifiedBackup(args[open + 1]);
            Pages.SelectedIndex = 4;
        }
        open = Array.IndexOf(args, "--open-rom");
        if (open >= 0 && open + 1 < args.Length)
        {
            LoadImage(args[open + 1]);
            Pages.SelectedIndex = 4;
        }
        UpdateLayout();
        int at = Array.IndexOf(args, "--screenshot");
        if (at >= 0 && at + 1 < args.Length)
        {
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(args[at + 1]);
            encoder.Save(file);
        }
        at = Array.IndexOf(args, "--smoke-report");
        if (at >= 0 && at + 1 < args.Length)
            File.WriteAllText(args[at + 1], JsonSerializer.Serialize(new
            {
                passed = true,
                version = Version,
                runtimeVersion = Environment.Version.ToString(),
                framework = "WPF / .NET 10",
                pages = 6,
                loggerChannels = loggerRows.Count,
                loggerProfiles = LoggerDefinition.Known.Count,
                editorDefinitions = definitionSnapshot.Editors.Count,
                definitionImportAvailable = DefinitionsButton.IsEnabled,
                importedDefinitions = definitionSnapshot.Entries.Count(entry => entry.Imported),
                loggerCalibration = loggerDefinition.SoftwareIds.Single(),
                stockRomLogger = true,
                realEcuLoggerValidated = false,
                supportedHardware = new[] { "Vgate vLinker FS", "Tactrix Openport 2.0" },
                processBits = IntPtr.Size * 8,
                tactrixSelectionVerified = true,
                mongooseSelectionVerified = true,
                experimentalHardware = new[] { "Mongoose-Plus ISO/CAN3" },
                mongooseHardwareTested = false,
                registeredDriverNames = DriverBox.Items.Cast<J2534Driver>().Select(item => item.Name).ToArray(),
                simulatorAvailable = false,
                liveProgrammingEnabled = false,
                noRomBundled = true,
                bundledHelpVerified = true,
                openedImageSha256 = image is null ? null : RomInspector.Hash(image),
                checksumCorrectionAvailable = CorrectButton.IsEnabled,
                backupReopened = args.Contains("--open-backup", StringComparer.Ordinal)
            }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
