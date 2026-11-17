// Adapted from SpeedLink (GPL-3.0-or-later); see ATTRIBUTIONS.md and LICENSE.
using System.Diagnostics;
using MazdaLink.Core;

namespace MazdaLink.J2534;

public sealed record J2534Version(string Firmware, string Driver, string Api);
public sealed record J2534Frame(uint Id, uint Status, byte[] Data);

/// <summary>A single physical ISO 15765 channel; implementations must not retry requests.</summary>
public interface IJ2534Channel : IDisposable
{
    string AdapterName => "J2534 adapter";
    bool Experimental => false;
    J2534Version Open();
    void Connect(uint bitrate, uint requestId, uint responseId);
    void ClearReceive();
    void Write(byte[] request);
    J2534Frame? Read(int timeoutMilliseconds);
    string? CleanupWarning { get; }
    object? ReadDiagnostics => null;
}

/// <summary>Read-only Mazda diagnostics through an installed J2534 driver.</summary>
public sealed class J2534ReadTransport : IReadTransport
{
    private readonly IJ2534Channel channel;
    private readonly TimeSpan responseTimeout;
    private readonly TimeSpan pendingTimeout;
    private bool initialized, disposed, faulted, identityConfirmed;
    private readonly IDiagnosticSink? diagnostics;
    public string Mode => "hardware";
    public string Adapter { get; private set; }
    public int ReadChunkSize => 512;
    public bool ReadAttempted { get; set; }
    public string? CloseWarning => channel.CleanupWarning;
    public string? CleanupWarning => channel.CleanupWarning;

    public J2534ReadTransport(IJ2534Channel channel, TimeSpan? responseTimeout = null, TimeSpan? pendingTimeout = null, IDiagnosticSink? diagnostics = null)
    {
        this.channel = channel;
        this.diagnostics = diagnostics;
        Adapter = channel.AdapterName;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);
        this.pendingTimeout = pendingTimeout ?? TimeSpan.FromSeconds(30);
        if (this.responseTimeout <= TimeSpan.Zero || this.pendingTimeout < this.responseTimeout)
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized || faulted)
            throw new TransportException("Reconnect the J2534 adapter before initializing again.");
        try
        {
            diagnostics?.Record("adapter.opening", new { adapter = channel.AdapterName, backend = "J2534", channel.Experimental });
            var version = channel.Open();
            diagnostics?.Record("adapter.version", new { version.Firmware, version.Driver, version.Api });
            if (channel.Experimental && !version.Api.StartsWith("04.04", StringComparison.Ordinal))
                throw new TransportException("This experimental adapter requires a J2534-1 04.04 driver. No CAN connection was opened.");
            channel.Connect(500000, 0x7E0, 0x7E8);
            Adapter = $"{channel.AdapterName} · firmware {version.Firmware} · driver {version.Driver} · API {version.Api}";
            initialized = true;
            diagnostics?.Record("adapter.ready", new { adapter = Adapter, readChunkBytes = ReadChunkSize, responseTimeoutMs = responseTimeout.TotalMilliseconds, pendingTimeoutMs = pendingTimeout.TotalMilliseconds });
        }
        catch (Exception error) { diagnostics?.Record("adapter.error", new { stage = "Open/configure J2534", error = error.Message }); faulted = true; Dispose(); throw; }
    }

    public byte[] Exchange(byte[] request)
    {
        // Validate before touching the driver, including clearing its receive queue.
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        if (!request.AsSpan().SequenceEqual(new byte[] { 9, 4 }) && !identityConfirmed)
            throw new TransportException("Read the ECU calibration identity before read-session requests.");
        if (request.AsSpan().SequenceEqual(new byte[] { 9, 4 })) identityConfirmed = false;
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!initialized || faulted)
            throw new TransportException("Reconnect and identify before continuing.");
        var total = Stopwatch.StartNew();
        var watch = new Stopwatch();
        int polls = 0, emptyPolls = 0, notifications = 0, foreignMessages = 0, completedReplies = 0, pendingReplies = 0;
        int txEchoes = 0, startNotifications = 0, txIndications = 0;
        try
        {
            channel.ClearReceive();
            channel.Write(request);
            watch.Start(); // Keep the ECU response deadline independent of driver queue/write time.
            var limit = responseTimeout;
            var packets = new List<byte[]>();
            while (watch.Elapsed < limit)
            {
                var remaining = Math.Max(1, (int)(limit - watch.Elapsed).TotalMilliseconds);
                var frame = channel.Read(Math.Min(25, remaining));
                polls++;
                if (frame is null)
                { emptyPolls++; continue; }
                // J2534 Tx echoes, start notifications and Tx indications are not PDUs.
                if ((frame.Status & 0x0B) != 0)
                {
                    notifications++;
                    if ((frame.Status & 1) != 0) txEchoes++;
                    if ((frame.Status & 2) != 0) startNotifications++;
                    if ((frame.Status & 8) != 0) txIndications++;
                    continue;
                }
                if (frame.Id != 0x7E8) { foreignMessages++; continue; }
                if ((frame.Status & 0x10) != 0)
                    throw new TransportException("J2534 adapter reported invalid ISO-TP frame padding.");
                // Diagnostics contain lengths/status only, never identity, seed/key or ROM payloads.
                diagnostics?.Record("j2534.response", new { canId = $"0x{frame.Id:X}", frame.Status, bytes = frame.Data.Length });
                if (frame.Data.Length is < 1 or > 4095)
                    throw new TransportException("Invalid J2534 response size.");
                packets.Add(frame.Data);
                if (packets.Count > 32)
                    throw new TransportException("Too many pending ECU replies. Reconnect before continuing.");
                if (frame.Data.AsSpan().SequenceEqual(new byte[] { 0x7F, request[0], 0x78 }))
                {
                    // A bounded pending interval extends this exchange, never resends it.
                    limit = pendingTimeout;
                    pendingReplies++;
                    continue;
                }
                completedReplies++;
                var final = DiagnosticProtocol.FinalResponse(request, packets);
                if (request.AsSpan().SequenceEqual(new byte[] { 9, 4 }))
                {
                    if (final.Length < 4 || final[1] != 4 || final[2] != 1) throw new TransportException("Invalid calibration identity reply.");
                    TargetPolicy.RequireIdentity(System.Text.Encoding.ASCII.GetString(final.AsSpan(3).TrimEnd((byte)0)));
                    identityConfirmed = true;
                }
                return final;
            }
            throw new TransportException($"J2534 response timed out after {watch.Elapsed.TotalMilliseconds:0} ms ({completedReplies} completed replies, {notifications} notifications, {pendingReplies} pending replies). No request was resent; reconnect before continuing.");
        }
        catch { faulted = true; throw; }
        finally { diagnostics?.Record("j2534.exchange", new { stage = DiagnosticProtocol.Describe(request), durationMs = total.Elapsed.TotalMilliseconds, responseWaitMs = watch.Elapsed.TotalMilliseconds,
            polls, emptyPolls, notifications, txEchoes, startNotifications, txIndications, foreignMessages, completedReplies, pendingReplies, driver = channel.ReadDiagnostics }); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        channel.Dispose();
    }
}
