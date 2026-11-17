// J2534 binding adapted from MazdaLink's GPL-3.0-or-later read binding.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Isolated raw-CAN bench binding. Its only transmit operations are entry and identification.</summary>
public sealed class NativeBenchMonitorChannel : IBenchMonitorChannel
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public uint ProtocolId, RxStatus, TxFlags, Timestamp, DataSize, ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)] public byte[] Data;
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
    public string? CleanupWarning { get; private set; }

    public NativeBenchMonitorChannel(J2534Driver driver)
    {
        var registered = J2534Drivers.Discover(driver.Kind).FirstOrDefault(d =>
            d.LibraryPath.Equals(Path.GetFullPath(driver.LibraryPath), StringComparison.OrdinalIgnoreCase) && d.RawCanSupported);
        libraryPath = registered?.LibraryPath ?? throw new TransportException("The registered Mongoose driver must advertise raw CAN support for the bench check.");
    }
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
            if (!api.ToString().StartsWith("04.04", StringComparison.Ordinal)) throw new TransportException("Bench mode requires J2534 04.04.");
            Check(connect(device, 5, 0x100, Zyj2RecoveryPolicy.Bitrate, out channel), "Connect bench extended CAN"); connected = true;
            var mask = Packet(0x1FFFFFFF, []); var pattern = Packet(Zyj2RecoveryPolicy.ResponseId, []);
            Check(startFilter(channel, 1, ref mask, ref pattern, 0, out filter), "Install bench pass filter"); filtered = true;
        }
        catch { Dispose(); throw; }
    }
    public void StartEntryPulse()
    {
        RequireReady();
        if (pulseAttempted) throw new TransportException("The entry pulse cannot be restarted on this channel.");
        byte[] payload = Zyj2RecoveryPolicy.EntryRequest;
        Zyj2RecoveryPolicy.CheckTransmit(payload, periodic: true);
        var packet = Packet(Zyj2RecoveryPolicy.RequestId, payload);
        pulseAttempted = true;
        Check(startPeriodic!(channel, ref packet, out periodic, Zyj2RecoveryPolicy.PeriodMilliseconds), "Start bounded entry pulse");
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
        uint status = stopPeriodic!(channel, periodic);
        if (status != 0) { Dispose(); throw new BenchCleanupException("Could not confirm entry-pulse stop. Disconnect both USB and ECU battery power before restarting MazdaLink."); }
        pulseStarted = false;
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
        Check(write!(channel, ref packet, ref count, 1000), "Request monitor identity");
        if (count != 1) throw new TransportException("The adapter did not accept exactly one monitor request.");
    }
    public RecoveryFrame? Read(int timeoutMilliseconds)
    {
        RequireReady();
        if (timeoutMilliseconds is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var packet = new Message { Data = new byte[4128] }; uint count = 1;
        uint status = read!(channel, ref packet, ref count, (uint)timeoutMilliseconds);
        if (status is not (9 or 16)) Check(status, "Receive monitor identity");
        if (count == 0) return null;
        if (count != 1 || packet.ProtocolId != 5) throw new TransportException("Invalid bench receive count or protocol.");
        if ((packet.RxStatus & 0x0B) != 0) return new(0, packet.RxStatus, []);
        if (packet.DataSize is < 4 or > 12 || packet.ExtraDataIndex > packet.DataSize)
            throw new TransportException("Malformed raw CAN bench message.");
        return new(BinaryPrimitives.ReadUInt32BigEndian(packet.Data), packet.RxStatus, packet.Data.AsSpan(4, (int)packet.DataSize - 4).ToArray());
    }
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
