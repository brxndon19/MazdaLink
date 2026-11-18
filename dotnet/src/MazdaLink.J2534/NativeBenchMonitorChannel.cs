// J2534 binding adapted from MazdaLink's GPL-3.0-or-later read binding.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Isolated raw-CAN bench binding. Its only transmit operations are entry and identification.</summary>
public sealed class NativeBenchMonitorChannel : IBenchMonitorChannel
{
    // SAE J2534-1 04.04: GET_CONFIG=0x01, SET_CONFIG=0x02, LOOPBACK=0x03.
    private const uint GetConfig = 0x01, SetConfig = 0x02;
    private const int LoopbackParameter = 0x03;
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public uint ProtocolId, RxStatus, TxFlags, Timestamp, DataSize, ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)] public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ConfigurationList { public uint Count; public nint Items; }
    private sealed class ReceiveCounters
    {
        public int Calls, EmptyReads, ReturnedFrames, TxEchoes, Notifications, ExpectedIdFrames;
        public uint Status, Count, Protocol, RxStatus, Size, ExtraIndex;
        public uint? CanId;
        public bool? PayloadMatched;
        public double MaxMilliseconds;
        public BenchReceiveDiagnostics Snapshot() => new(Calls, EmptyReads, ReturnedFrames, TxEchoes,
            Notifications, ExpectedIdFrames, Status, Count, Protocol, RxStatus, Size, ExtraIndex,
            CanId, PayloadMatched, MaxMilliseconds);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint OpenFn(nint name, out uint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint CloseFn(uint device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ConnectFn(uint device, uint protocol, uint flags, uint baud, out uint channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint DisconnectFn(uint channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadFn(uint channel, ref Message message, ref uint count, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint WriteFn(uint channel, ref Message message, ref uint count, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint FilterFn(uint channel, uint type, ref Message mask, ref Message pattern, nint flow, out uint filter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint StopFn(uint channel, uint identifier);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint PeriodicFn(uint channel, ref Message message, out uint identifier, uint interval);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint VersionFn(uint device, StringBuilder firmware, StringBuilder dll, StringBuilder api);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint IoctlFn(uint channel, uint code, nint input, nint output);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string path, nint reserved, uint flags);

    private readonly string libraryPath;
    private nint module;
    private uint device, channel, filter, periodic;
    private bool opened, connected, filtered, pulseAttempted, pulseStarted, identityRequested, disposed;
    private CloseFn? close;
    private DisconnectFn? disconnect;
    private ReadFn? read;
    private WriteFn? write;
    private StopFn? stopFilter, stopPeriodic;
    private PeriodicFn? startPeriodic;
    private IoctlFn? ioctl;
    private string? adapterName, firmwareVersion, driverVersion, apiVersion;
    private uint? loopbackStatus;
    private readonly bool allowRamSelfTest;
    private bool initialMonitorIdentified, ramFaulted;
    private int nextRamStep, verifiedRamBytes;
    private RamSelfTestStep? awaitingRamStep;
    internal RamSelfTestTransportDiagnostics RamDiagnostics => new(nextRamStep, verifiedRamBytes, awaitingRamStep is not null, ramFaulted);
    private BenchConfigurationReading? loopbackReadback;
    private BenchSupplyReading? beforeEntrySupply, afterEntrySupply;
    private ReceiveCounters? entryReceive, identityReceive;
    private int startCalls, stopCalls, identityRequests;
    private long? entryStarted;
    private double startMilliseconds, stopMilliseconds, identityMilliseconds;
    private double? entryActiveMilliseconds;
    private int[]? entryEchoBins, entryFrameBins;
    public string? CleanupWarning { get; private set; }
    public BenchChannelDiagnostics Diagnostics => new(adapterName, firmwareVersion, driverVersion, apiVersion,
        Zyj2RecoveryPolicy.Bitrate, Zyj2RecoveryPolicy.RequestId, Zyj2RecoveryPolicy.ResponseId,
        loopbackStatus, loopbackReadback, beforeEntrySupply, afterEntrySupply, startCalls, stopCalls, identityRequests,
        startMilliseconds, stopMilliseconds, identityMilliseconds, entryActiveMilliseconds,
        (entryReceive ?? new()).Snapshot(), (identityReceive ?? new()).Snapshot(),
        Enumerable.Range(0, 32).Where(i => (entryEchoBins?[i] ?? 0) + (entryFrameBins?[i] ?? 0) > 0)
            .Select(i => new BenchTrafficBin(i, entryEchoBins?[i] ?? 0, entryFrameBins?[i] ?? 0)).ToArray());

    public NativeBenchMonitorChannel(J2534Driver driver) : this(driver, false) { }
    internal NativeBenchMonitorChannel(J2534Driver driver, bool allowRamSelfTest)
    {
        this.allowRamSelfTest = allowRamSelfTest;
        var registered = J2534Drivers.Discover(driver.Kind).FirstOrDefault(d =>
            d.LibraryPath.Equals(Path.GetFullPath(driver.LibraryPath), StringComparison.OrdinalIgnoreCase) && d.RawCanSupported);
        libraryPath = registered?.LibraryPath ?? throw new TransportException("The registered Mongoose driver must advertise raw CAN support for the bench check.");
        adapterName = Bounded(registered.Name);
    }
    private static string Bounded(string value) => value[..Math.Min(value.Length, 128)];
    private T Bind<T>(string name, int bytes) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(module, name, out var address) &&
            !NativeLibrary.TryGetExport(module, $"_{name}@{bytes}", out address))
            throw new TransportException("J2534 bench export missing: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void Check(uint status, string action)
    {
        if (status != 0) throw new TransportException($"{action}: J2534 0x{status:X}. The bench check stopped; no automatic reconnect was attempted.");
    }
    private static Message Packet(uint id, ReadOnlySpan<byte> payload)
    {
        var packet = new Message { ProtocolId = 5, TxFlags = 0x100, DataSize = (uint)payload.Length + 4, Data = new byte[4128] };
        BinaryPrimitives.WriteUInt32BigEndian(packet.Data, id);
        payload.CopyTo(packet.Data.AsSpan(4));
        return packet;
    }
    private void RequireReady()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!opened || !connected || !filtered) throw new TransportException("The bench CAN channel is not ready.");
    }
    public void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4 || opened || module != 0)
            throw new TransportException("The bench channel requires a fresh 32-bit Windows J2534 connection.");
        try
        {
            module = LoadLibraryExW(libraryPath, 0, 0x100 | 0x800);
            if (module == 0) throw new TransportException("Cannot load the registered Mongoose bench driver.");
            var open = Bind<OpenFn>("PassThruOpen", 8);
            close = Bind<CloseFn>("PassThruClose", 4);
            var connect = Bind<ConnectFn>("PassThruConnect", 20);
            disconnect = Bind<DisconnectFn>("PassThruDisconnect", 4);
            read = Bind<ReadFn>("PassThruReadMsgs", 16);
            write = Bind<WriteFn>("PassThruWriteMsgs", 16);
            var startFilter = Bind<FilterFn>("PassThruStartMsgFilter", 24);
            stopFilter = Bind<StopFn>("PassThruStopMsgFilter", 8);
            startPeriodic = Bind<PeriodicFn>("PassThruStartPeriodicMsg", 16);
            stopPeriodic = Bind<StopFn>("PassThruStopPeriodicMsg", 8);
            ioctl = Bind<IoctlFn>("PassThruIoctl", 16);
            var version = Bind<VersionFn>("PassThruReadVersion", 16);
            Check(open(0, out device), "Open bench adapter"); opened = true;
            var fw = new StringBuilder(256); var dll = new StringBuilder(256); var api = new StringBuilder(256);
            Check(version(device, fw, dll, api), "Read bench driver version");
            firmwareVersion = Bounded(fw.ToString()); driverVersion = Bounded(dll.ToString()); apiVersion = Bounded(api.ToString());
            if (!api.ToString().StartsWith("04.04", StringComparison.Ordinal)) throw new TransportException("Bench mode requires J2534 04.04.");
            Check(connect(device, 5, 0x100, Zyj2RecoveryPolicy.Bitrate, out channel), "Connect bench extended CAN"); connected = true;
            var mask = Packet(0x1FFFFFFF, []); var pattern = Packet(Zyj2RecoveryPolicy.ResponseId, []);
            Check(startFilter(channel, 1, ref mask, ref pattern, 0, out filter), "Install bench pass filter"); filtered = true;
            ConfigureDiagnostics();
        }
        catch { Dispose(); throw; }
    }
    private void ConfigureDiagnostics()
    {
        // SAE J2534 SET_CONFIG / LOOPBACK=1 requests adapter TX echoes only.
        // It sends no extra ECU messages; echo availability is not an ECU acknowledgement.
        nint item = Marshal.AllocHGlobal(8), list = Marshal.AllocHGlobal(Marshal.SizeOf<ConfigurationList>());
        try
        {
            Marshal.WriteInt32(item, LoopbackParameter); Marshal.WriteInt32(item, 4, 1);
            Marshal.StructureToPtr(new ConfigurationList { Count = 1, Items = item }, list, false);
            loopbackStatus = ioctl!(channel, SetConfig, list, 0);
            // A successful SET is not a readback. Sentinel protects against a
            // driver returning success without filling GET_CONFIG's in/out list.
            Marshal.WriteInt32(item, LoopbackParameter); Marshal.WriteInt32(item, 4, -1);
            Marshal.StructureToPtr(new ConfigurationList { Count = 1, Items = item }, list, false);
            uint readStatus = ioctl!(channel, GetConfig, list, 0);
            int value = Marshal.ReadInt32(item, 4);
            loopbackReadback = new(readStatus,
                readStatus == 0 && Marshal.ReadInt32(list) == 1 &&
                Marshal.ReadInt32(item) == LoopbackParameter && value is 0 or 1 ? (uint)value : null);
        }
        finally { Marshal.FreeHGlobal(list); Marshal.FreeHGlobal(item); }
        beforeEntrySupply = ReadSupply();
    }
    private BenchSupplyReading ReadSupply()
    {
        // Optional adapter-only measurement, always outside the active entry window.
        nint result = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(result, -1); // An untouched output must not look like a real 0 V reading.
            uint status = ioctl!(device, 3, 0, result);
            int millivolts = Marshal.ReadInt32(result);
            return new(status, status == 0 && millivolts is >= 0 and <= 32000 ? millivolts : null);
        }
        finally { Marshal.FreeHGlobal(result); }
    }
    public void StartEntryPulse()
    {
        RequireReady();
        if (pulseAttempted) throw new TransportException("The entry pulse cannot be restarted on this channel.");
        byte[] payload = Zyj2RecoveryPolicy.EntryRequest;
        Zyj2RecoveryPolicy.CheckTransmit(payload, periodic: true);
        var packet = Packet(Zyj2RecoveryPolicy.RequestId, payload);
        pulseAttempted = true;
        startCalls++; entryStarted = Stopwatch.GetTimestamp();
        uint status;
        try { status = startPeriodic!(channel, ref packet, out periodic, Zyj2RecoveryPolicy.PeriodMilliseconds); }
        finally { startMilliseconds = Stopwatch.GetElapsedTime(entryStarted.Value).TotalMilliseconds; }
        Check(status, "Start bounded entry pulse");
        pulseStarted = true;
    }
    public void StopEntryPulse()
    {
        RequireReady();
        if (!pulseStarted)
        {
            // A failed start can leave driver state uncertain. Disconnect instead of identifying.
            if (pulseAttempted) { Dispose(); throw new TransportException("Entry start failed; bench channel was disconnected."); }
            return;
        }
        stopCalls++; long stopping = Stopwatch.GetTimestamp();
        uint status;
        try { status = stopPeriodic!(channel, periodic); }
        finally
        {
            stopMilliseconds = Stopwatch.GetElapsedTime(stopping).TotalMilliseconds;
            if (entryStarted is long began) entryActiveMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        }
        if (status != 0) { Dispose(); throw new BenchCleanupException("Could not confirm entry-pulse stop. Disconnect both USB and ECU battery power before restarting MazdaLink."); }
        pulseStarted = false;
        afterEntrySupply = ReadSupply();
    }
    public void ClearReceive()
    {
        RequireReady();
        if (pulseStarted) throw new TransportException("Stop entry traffic before requesting identification.");
        Check(ioctl!(channel, 8, 0, 0), "Clear bench receive queue");
    }
    public void RequestIdentity()
    {
        RequireReady();
        if (pulseStarted || !pulseAttempted || identityRequested) throw new TransportException("Complete one entry window before the single identification request. Requests are not retried.");
        byte[] payload = Zyj2RecoveryPolicy.IdentityRequest;
        Zyj2RecoveryPolicy.CheckTransmit(payload, periodic: false);
        var packet = Packet(Zyj2RecoveryPolicy.RequestId, payload); uint count = 1;
        identityRequested = true;
        identityRequests++; long writing = Stopwatch.GetTimestamp();
        uint status;
        try { status = write!(channel, ref packet, ref count, 1000); }
        finally { identityMilliseconds = Stopwatch.GetElapsedTime(writing).TotalMilliseconds; }
        Check(status, "Request monitor identity");
        if (count != 1) throw new TransportException("The adapter did not accept exactly one monitor request.");
    }
    public RecoveryFrame? Read(int timeoutMilliseconds)
    {
        RequireReady();
        if (timeoutMilliseconds is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var packet = new Message { Data = new byte[4128] }; uint count = 1;
        var stats = pulseStarted ? entryReceive ??= new() : identityReceive ??= new();
        stats.Calls++;
        long reading = Stopwatch.GetTimestamp();
        uint status = read!(channel, ref packet, ref count, (uint)timeoutMilliseconds);
        stats.MaxMilliseconds = Math.Max(stats.MaxMilliseconds, Stopwatch.GetElapsedTime(reading).TotalMilliseconds);
        stats.Status = status; stats.Count = count; stats.Protocol = packet.ProtocolId;
        stats.RxStatus = packet.RxStatus; stats.Size = packet.DataSize; stats.ExtraIndex = packet.ExtraDataIndex;
        stats.CanId = null; stats.PayloadMatched = null;
        if (status is not (9 or 16)) Check(status, "Receive monitor identity");
        if (count == 0) { stats.EmptyReads++; return null; }
        if (count != 1 || packet.ProtocolId != 5) throw new TransportException("Invalid bench receive count or protocol.");
        stats.ReturnedFrames++;
        if (pulseStarted && entryStarted is long began)
        {
            int second = (int)Math.Clamp(Stopwatch.GetElapsedTime(began).TotalSeconds, 0, 31);
            if ((packet.RxStatus & 1) != 0) (entryEchoBins ??= new int[32])[second]++;
            else (entryFrameBins ??= new int[32])[second]++;
        }
        if ((packet.RxStatus & 1) != 0) stats.TxEchoes++;
        if ((packet.RxStatus & 0x0A) != 0) stats.Notifications++;
        if ((packet.RxStatus & 0x0B) != 0) return new(0, packet.RxStatus, []);
        if (packet.DataSize is < 4 or > 12 || packet.ExtraDataIndex > packet.DataSize)
            throw new TransportException("Malformed raw CAN bench message.");
        uint id = BinaryPrimitives.ReadUInt32BigEndian(packet.Data);
        stats.CanId = id;
        var payload = packet.Data.AsSpan(4, (int)packet.DataSize - 4);
        if (id == Zyj2RecoveryPolicy.ResponseId)
        {
            stats.ExpectedIdFrames++;
            stats.PayloadMatched = payload.SequenceEqual(Zyj2RecoveryPolicy.ExpectedIdentity);
        }
        var frame = new RecoveryFrame(id, packet.RxStatus, payload.ToArray());
        if (identityRequested && !pulseStarted && id == Zyj2RecoveryPolicy.ResponseId && packet.RxStatus == 0x100 &&
            payload.SequenceEqual(Zyj2RecoveryPolicy.ExpectedIdentity)) initialMonitorIdentified = true;
        if (awaitingRamStep is not null)
        {
            if (!awaitingRamStep.MatchesReply(frame)) ramFaulted = true;
            else
            {
                if (awaitingRamStep.Operation == RamSelfTestOperation.VerifyByte) verifiedRamBytes++;
                awaitingRamStep = null;
            }
        }
        return frame;
    }
    // Only the separate NativeRamSelfTestChannel enables this capability.
    // The identification backend's public interface has no upload/execute method.
    internal void SendFixedRamSelfTestStep(int index)
    {
        RequireReady();
        if (!allowRamSelfTest || !initialMonitorIdentified || pulseStarted || ramFaulted ||
            awaitingRamStep is not null || index != nextRamStep || index < 0 || index >= Zyj2RamSelfTestPolicy.Steps.Count)
        { ramFaulted = true; throw new TransportException("Fixed RAM self-test step blocked by capability, monitor or sequence gate."); }
        var step = Zyj2RamSelfTestPolicy.Steps[index];
        if (step.Operation == RamSelfTestOperation.ExecuteFixedTest && verifiedRamBytes != Zyj2RamSelfTestPolicy.Image.Length)
        { ramFaulted = true; throw new TransportException("Every fixed RAM image byte must be read back before execution."); }
        var packet = Packet(Zyj2RecoveryPolicy.RequestId, step.Payload); uint count = 1;
        nextRamStep++;
        if (step.Operation == RamSelfTestOperation.IdentifyMonitor) identityRequests++;
        awaitingRamStep = step.ExpectsReply ? step : null;
        try
        {
            Check(write!(channel, ref packet, ref count, 1000), "Send fixed RAM self-test step");
            if (count != 1) throw new TransportException("Fixed RAM self-test write count was not one. No retry was attempted.");
        }
        catch { ramFaulted = true; throw; }
    }
    internal void FailRamSelfTest() => ramFaulted = true;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var warnings = new List<string>();
        try
        {
            if (pulseStarted && stopPeriodic is not null && stopPeriodic(channel, periodic) != 0) warnings.Add("Entry pulse stop failed.");
            if (filtered && stopFilter is not null && stopFilter(channel, filter) != 0) warnings.Add("Bench filter cleanup failed.");
            if (connected && disconnect is not null && disconnect(channel) != 0) warnings.Add("Bench disconnect failed.");
            if (opened && close is not null && close(device) != 0) warnings.Add("Bench adapter close failed.");
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            module = 0; opened = connected = filtered = pulseStarted = false;
            CleanupWarning = warnings.Count == 0 ? null : string.Join(' ', warnings);
        }
    }
}
