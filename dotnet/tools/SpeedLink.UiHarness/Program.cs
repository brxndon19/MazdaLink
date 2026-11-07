using System.IO;
using System.Text.Json;
using System.Windows;
using SpeedLink.Desktop;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string Get(string key)
        {
            int at = Array.IndexOf(args, key);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException("Missing " + key);
        }
        if (args.Contains("--logger-render-offline", StringComparer.Ordinal))
        {
            var renderApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            renderApp.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                try
                {
                    string output = Get("--output"); Directory.CreateDirectory(output);
                    window = new MainWindow(Path.Combine(output, "data")); renderApp.MainWindow = window;
                    var report = await window.ExerciseLoggerRendering(1000, args.Contains("--require-stable", StringComparer.Ordinal));
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.CloseWorkbenchTest(); renderApp.Shutdown(resultCode); }
            };
            renderApp.Run(); return resultCode;
        }
        if (args.Contains("--read-progress-offline", StringComparer.Ordinal))
        {
            var readApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            readApp.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                try
                {
                    string output = Get("--output"); Directory.CreateDirectory(output);
                    using var peer = new ReadProgressPeer(Get("--scenario"), args.Contains("--gen2", StringComparer.Ordinal), args.Contains("--unknown-cal", StringComparer.Ordinal), args.Contains("--chassis-id", StringComparer.Ordinal));
                    window = new MainWindow(Path.Combine(output, "data")); readApp.MainWindow = window;
                    var report = await window.ExerciseReadProgressOffline(peer, output, Get("--scenario"), args.Contains("--unknown-cal", StringComparer.Ordinal));
                    report["memoryRequests"] = peer.MemoryRequests;
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.CloseWorkbenchTest(); readApp.Shutdown(resultCode); }
            };
            readApp.Run(); return resultCode;
        }
        if (args.Contains("--menus-offline", StringComparer.Ordinal) || args.Contains("--support-offline", StringComparer.Ordinal))
        {
            var menuApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            menuApp.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                try
                {
                    string output = Get("--output"); Directory.CreateDirectory(output);
                    window = new MainWindow(Path.Combine(output, "data")); menuApp.MainWindow = window;
                    var report = args.Contains("--support-offline", StringComparer.Ordinal) ? await window.ExerciseDiagnosticsOffline(output) : await window.ExerciseMenusOffline(output);
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.CloseWorkbenchTest(); menuApp.Shutdown(resultCode); }
            };
            menuApp.Run(); return resultCode;
        }
        if (args.Contains("--replay-offline", StringComparer.Ordinal))
        {
            var replayApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            replayApp.Startup += async (_, _) =>
            {
                LogViewerWindow? window = null;
                try
                {
                    window = new(); replayApp.MainWindow = window;
                    var report = await window.ExerciseOffline(Get("--file"), Get("--screenshot"));
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.Close(); replayApp.Shutdown(resultCode); }
            };
            replayApp.Run(); return resultCode;
        }
        if (args.Contains("--axes-offline", StringComparer.Ordinal) || args.Contains("--navigation-offline", StringComparer.Ordinal) || args.Contains("--scalar-cards-offline", StringComparer.Ordinal) || args.Contains("--map-windows-offline", StringComparer.Ordinal) || args.Contains("--editor-offline", StringComparer.Ordinal) || args.Contains("--workbench-offline", StringComparer.Ordinal) || args.Contains("--definitions-offline", StringComparer.Ordinal) || args.Contains("--layout-offline", StringComparer.Ordinal) || args.Contains("--reliability-offline", StringComparer.Ordinal))
        {
            var editorApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            editorApp.DispatcherUnhandledException += (_, failure) =>
            {
                File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, fatalUiException = true, error = failure.Exception.ToString() }));
                failure.Handled = true;
                editorApp.Shutdown(1);
            };
            editorApp.Startup += async (_, _) =>
            {
                EditorWindow? window = null; MainWindow? workbench = null;
                try
                {
                    var profile = SpeedLink.Core.RomInspector.KnownProfiles.Single(p => p.Calibration == Get("--calibration"));
                    var definition = SpeedLink.Core.TuningDefinition.Known.Single(d => d.Calibration == profile.Calibration);
                    var image = new byte[SpeedLink.Core.RomInspector.RomSize];
                    "SYNTHETIC UI TEST - NOT ECU FIRMWARE"u8.CopyTo(image.AsSpan(0x100));
                    System.Text.Encoding.ASCII.GetBytes(profile.Identity).CopyTo(image, profile.IdentityOffset);
                    image[0x8002] = 0x5A; image[0x8003] = 0xA5; image[^2] = 0xA5; image[^1] = 0x5A;
                    foreach (var word in profile.LayoutWords) System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(word.Address, 4), word.Value);
                    foreach (var marker in profile.Markers ?? []) System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(marker.Address, 2), marker.Value);
                    int fixtureBalance = profile.CorrectionAddress >= 0 ? profile.CorrectionAddress : 0xBFFF0;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(fixtureBalance, 4), unchecked(SpeedLink.Core.RomInspector.ChecksumTarget - SpeedLink.Core.RomInspector.Sum(image, profile)));
                    profile = profile with { BootSha256 = SpeedLink.Core.RomInspector.BootHash(image, profile), LoopSha256 = profile.RoutineRanges is not null ? SpeedLink.Core.RomInspector.Hash(profile.RoutineRanges.SelectMany(r => image.Skip(r.Address).Take(r.Length)).ToArray()) : profile.LoopAddress is int at ? SpeedLink.Core.RomInspector.Hash(image.AsSpan(at, profile.LoopSize!.Value)) : null };
                    var guard = new SpeedLink.Core.TuningCodeGuard([new(profile.Calibration, profile.ChecksumStart, SpeedLink.Core.TuningCodeGuard.Hash(image, definition, profile))]);
                    string output = Get("--output"); Directory.CreateDirectory(output);
                    string path = Path.Combine(output, "SYNTHETIC_NOT_ECU_FIRMWARE_" + profile.Calibration + ".bin"); File.WriteAllBytes(path, image);
                    object workflow;
                    if (args.Contains("--axes-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseEveryTableAxesOffline(path, output, args.Contains("--table", StringComparer.Ordinal) ? Get("--table") : null, args.Contains("--panes-only", StringComparer.Ordinal));
                    }
                    else if (args.Contains("--navigation-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseNavigationOffline(path, output);
                    }
                    else if (args.Contains("--scalar-cards-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workbench.Show(); workbench.OpenEditorFile(path); workbench.UpdateLayout();
                        workflow = new { passed = true, cases = await workbench.EditorPane.ExerciseCompactScalarPanes(output) };
                    }
                    else if (args.Contains("--map-windows-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseMapWindowsOffline(path, output);
                    }
                    else if (args.Contains("--reliability-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        SpeedLink.Core.IReadTransport CreatePeer(SpeedLink.Core.LoggerDefinition selected)
                        {
                            var peer = new LoggerPeer(selected, Path.Combine(Get("--vectors-root"), selected.SoftwareIds.Single() + ".vectors.json"), false);
                            peer.AllowTraffic(); return peer;
                        }
                        workflow = await workbench.ExerciseReliabilityOffline(path, output, Get("--recording"), CreatePeer);
                        workbench = null; // The workflow verifies normal close and a fresh window.
                    }
                    else if (args.Contains("--layout-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseLayoutOffline(path, output);
                    }
                    else if (args.Contains("--definitions-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseDefinitionsOffline(path, output, profile.Calibration);
                    }
                    else if (args.Contains("--workbench-offline", StringComparer.Ordinal))
                    {
                        workbench = new MainWindow(Path.Combine(output, "data"), new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = workbench;
                        workflow = await workbench.ExerciseWorkbenchOffline(path, output, args.Contains("--recording", StringComparer.Ordinal) ? Get("--recording") : null);
                    }
                    else
                    {
                        window = new(new SpeedLink.Core.RomInspector([profile], guard), guard); editorApp.MainWindow = window;
                        workflow = await window.ExerciseOffline(path, output);
                    }
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = true, synthetic = true, hardwareAccessed = false, calibration = profile.Calibration, workflow }, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.CloseOfflineTest(); workbench?.CloseWorkbenchTest(); editorApp.Shutdown(resultCode); }
            };
            editorApp.Run(); return resultCode;
        }
        if (args.Contains("--logger-offline", StringComparer.Ordinal))
        {
            var loggerApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int resultCode = 1;
            loggerApp.Startup += async (_, _) =>
            {
                MainWindow? loggerWindow = null;
                try
                {
                    bool fault = args.Contains("--fault", StringComparer.Ordinal);
                    var profile = SpeedLink.Core.LoggerDefinition.Load(Get("--definition"));
                    using var vectors = JsonDocument.Parse(File.ReadAllBytes(Get("--vectors")));
                    var values = vectors.RootElement.GetProperty("calibration");
                    var calibration = SpeedLink.Core.LoggerCalibration.ExplicitMap(values.GetProperty("map_gain_kpa_per_v").GetDouble(), values.GetProperty("map_offset_kpa").GetDouble());
                    using var peer = new LoggerPeer(profile, Get("--vectors"), fault);
                    loggerWindow = new MainWindow(Get("--vault")); loggerApp.MainWindow = loggerWindow;
                    string? romPath = args.Contains("--logger-rom", StringComparer.Ordinal) ? Get("--logger-rom") : null;
                    int? requestsBeforeIdentityChange = null;
                    Action? changeIdentity = args.Contains("--identity-change", StringComparer.Ordinal) ? () => { requestsBeforeIdentityChange = peer.Requests.Count; peer.IdentityChanged = true; } : null;
                    var result = await loggerWindow.ExerciseLoggerOffline(peer, Get("--screenshot"), fault, profile, calibration, peer.AllowTraffic, romPath, changeIdentity);
                    if (requestsBeforeIdentityChange is int before && peer.Requests.Count != before) throw new InvalidOperationException("Changed ECU received logger queries before identity rejection.");
                    result["requests"] = peer.Requests; result["faultRequests"] = peer.FaultRequests;
                    if (fault && peer.FaultRequests != 1) throw new InvalidOperationException("Faulted request was retried.");
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { loggerWindow?.Close(); loggerApp.Shutdown(resultCode); }
            };
            loggerApp.Run(); return resultCode;
        }
        if (args.Contains("--real-ecu-logger", StringComparer.Ordinal))
        {
            var loggerApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int resultCode = 1;
            loggerApp.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                try
                {
                    window = new MainWindow(Get("--vault")); loggerApp.MainWindow = window;
                    int seconds = args.Contains("--throughput-seconds", StringComparer.Ordinal) ? int.Parse(Get("--throughput-seconds")) : 0;
                    if (seconds is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(seconds));
                    int soak = args.Contains("--soak-seconds", StringComparer.Ordinal) ? int.Parse(Get("--soak-seconds")) : 0;
                    if (soak is < 0 or > 900) throw new ArgumentOutOfRangeException(nameof(soak));
                    var result = await window.ExerciseLoggerHardware(Get("--port"), Get("--rom"), Get("--screenshot"), args.Contains("--tactrix", StringComparer.Ordinal), realEcu: true, performanceSeconds: seconds, soakSeconds: soak);
                    File.WriteAllText(Get("--report"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); resultCode = 0;
                }
                catch (Exception error) { File.WriteAllText(Get("--report"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
                finally { window?.Close(); loggerApp.Shutdown(resultCode); }
            };
            loggerApp.Run(); return resultCode;
        }
        if (!args.Contains("--isolated-bench", StringComparer.Ordinal))
            throw new ArgumentException("Confirmed isolated bench is required.");
        var report = Get("--report");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int code = 1;
        app.Startup += async (_, _) =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(Get("--vault"));
                app.MainWindow = window;
                int baudAt = Array.IndexOf(args, "--baud");
                int baud = baudAt < 0 ? 115200 : int.Parse(args[baudAt + 1]);
                int adapterAt = Array.IndexOf(args, "--adapter");
                bool tactrix = adapterAt >= 0 && args[adapterAt + 1] == "tactrix";
                var result = args.Contains("--logger-hardware", StringComparer.Ordinal)
                    ? await window.ExerciseLoggerHardware(Get("--port"), Get("--expected"), Get("--screenshot"), tactrix)
                    : await window.ExerciseHardware(Get("--port"), File.ReadAllBytes(Get("--expected")), baud, tactrix);
                File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                code = 0;
            }
            catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
            finally { window?.Close(); app.Shutdown(code); }
        };
        app.Run();
        return code;
    }
}

internal sealed class LoggerPeer : SpeedLink.Core.IReadTransport
{
    private readonly System.Threading.ManualResetEventSlim ready = new(false);
    public void AllowTraffic() => ready.Set();
    private readonly Dictionary<string, byte[]> replies = [];
    private readonly bool fault;
    public List<string> Requests { get; } = [];
    public int FaultRequests { get; private set; }
    public string Mode => "test";
    public string Adapter => "Offline logger UI fixture";
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    private readonly string calibrationIdentity;
    public bool IdentityChanged { get; set; }
    public LoggerPeer(SpeedLink.Core.LoggerDefinition definition, string vectors, bool fault)
    {
        this.fault = fault;
        calibrationIdentity = SpeedLink.Core.RomInspector.KnownProfiles.Single(p => p.Calibration == definition.SoftwareIds.Single()).Identity;
        using var data = JsonDocument.Parse(File.ReadAllBytes(vectors));
        foreach (var vector in data.RootElement.GetProperty("vectors").EnumerateArray())
            if (vector.GetProperty("type").GetString() == "decode")
                replies.TryAdd(Convert.ToHexString(definition.RequestFor(vector.GetProperty("channel").GetString()!)),
                    Convert.FromHexString(vector.GetProperty("responseHex").GetString()!));
    }
    public byte[] Exchange(byte[] request)
    {
        if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Offline UI did not enable fixture traffic.");
        SpeedLink.Core.DiagnosticProtocol.CheckReadOnly(request);
        if (request.AsSpan().SequenceEqual(new byte[] { 0x09, 0x02 })) throw new SpeedLink.Core.NegativeResponseException(0x09, 0x11);
        if (request.AsSpan().SequenceEqual(new byte[] { 0x09, 0x04 })) return [0x49, 0x04, 1, .. System.Text.Encoding.ASCII.GetBytes(IdentityChanged ? "UNSUPPORTED" : calibrationIdentity)];
        if (!(request.Length == 3 && request[0] == 0x22) && !SpeedLink.Core.Mode01LoggerProtocol.IsAllowed(request)) throw new InvalidOperationException("Logger attempted a non-logging request.");
        string key = Convert.ToHexString(request); Requests.Add(key);
        if (fault && Requests.Count >= 4) { FaultRequests++; throw new SpeedLink.Core.TransportException("Offline injected USB removal"); }
        if (request[0] == 1) return [0x41, .. request.Skip(1).SelectMany(id => new[] { id }.Concat(replies["2200" + id.ToString("X2")].Skip(3)))];
        return replies[key];
    }
    public void Dispose() => ready.Dispose();
}

internal sealed class ReadProgressPeer(string scenario, bool gen2 = false, bool unknown = false, bool chassis = false) : SpeedLink.Core.IReadTransport
{
    private readonly byte[] memory = CreateMemory();
    private static byte[] CreateMemory()
    {
        var data = new byte[SpeedLink.Core.RomInspector.RomSize];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 17);
        "SYNTHETIC UI TEST - NOT ECU FIRMWARE"u8.CopyTo(data.AsSpan(0x100));
        return data;
    }
    public string Mode => "simulator";
    public string Adapter => "Offline read-progress fixture";
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    public int MemoryRequests { get; private set; }
    public byte[] Exchange(byte[] request)
    {
        SpeedLink.Core.DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        switch (Convert.ToHexString(request))
        {
            case "0902": return [0x49, 0x02, 1, .. (chassis ? "GG3P123456       "u8.ToArray() : "JM1GG12L061000001"u8.ToArray())];
            case "0904": return [0x49, 0x04, 1, .. (gen2 && !unknown ? "L3CGEE000L3T6060"u8.ToArray() : "UNKNOWN01"u8.ToArray())];
            case "010C": return [0x41, 0x0C, 0, 0];
            case "1087":
                if (gen2) throw new InvalidOperationException("Gen 1 session used for Gen 2.");
                if (scenario == "preflight") throw new SpeedLink.Core.TransportException("Synthetic session-entry failure");
                return [0x50, 0x87];
            case "2701": return [0x67, 1, 0, 0, 0];
            case "1003":
                if (!gen2) throw new InvalidOperationException("Gen 2 session used for Gen 1.");
                if (scenario == "preflight") throw new SpeedLink.Core.TransportException("Synthetic session-entry failure");
                return [0x50, 3, 0, 50, 1, 244];
            case "2703": return [0x67, 3, 0x12, 0x34, 0x56];
            case "270486CA06": return [0x67, 4];
        }
        if (request[0] != 0x23 || request.Length != (gen2 ? 8 : 7) || gen2 && request[1] != 0x24) throw new InvalidOperationException("Unexpected offline request.");
        MemoryRequests++;
        if (scenario == "timeout" && MemoryRequests == 15) throw new SpeedLink.Core.TransportException("Synthetic receive timeout; no retry");
        int start = gen2 ? 2 : 1;
        int address = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(start, 4));
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(start + 4, 2));
        byte[] response = [0x63, .. memory.AsSpan(address, count).ToArray()];
        if (scenario == "short" && MemoryRequests == 15) return response[..^1];
        if (scenario == "mismatch" && MemoryRequests == (gen2 ? 1027 : 266)) response[^1] ^= 1;
        System.Threading.Thread.Sleep(5);
        return response;
    }
    public void Dispose() { }
}
