// Dedicated Gen 1 programming transport. Other J2534 channels remain read-only.
using System.Diagnostics;
using SpeedLink.Core;

namespace SpeedLink.J2534;

public sealed class BenchFlashTransport : IBenchFlashTransport
{
    private readonly IJ2534Channel channel;
    private readonly BenchRequestPermit permit;
    private readonly Func<int> voltage;
    private readonly TimeSpan responseTimeout, pendingTimeout;
    private bool initialized, disposed, faulted;
    public string Mode => "hardware";
    public string Adapter { get; private set; }
    public int ReadChunkSize => 3968;
    public bool ReadAttempted { get; set; }
    public string? CloseWarning => channel.CleanupWarning;
    public int BatteryMillivolts => initialized && !faulted && !disposed ? voltage() : throw new TransportException("Reconnect before checking supply.");
    public static BenchFlashTransport Create(J2534Driver driver)
    {
        if (driver.Kind is not (J2534AdapterKind.Tactrix or J2534AdapterKind.MongoosePlusIsoCan3))
            throw new TransportException("Select a registered Tactrix or Mongoose-Plus ISO/CAN3 driver.");
        var permit = new BenchRequestPermit();
        var channel = new NativeJ2534Channel(driver, diagnostics: null, programmingPermit: permit);
        return new(channel, permit, channel.ReadBatteryMillivolts);
    }
    public BenchFlashTransport(IJ2534Channel channel, BenchRequestPermit permit, Func<int> voltage,
        TimeSpan? responseTimeout = null, TimeSpan? pendingTimeout = null)
    {
        this.channel = channel; this.permit = permit; this.voltage = voltage; Adapter = channel.AdapterName;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);
        this.pendingTimeout = pendingTimeout ?? TimeSpan.FromSeconds(120);
        if (this.responseTimeout <= TimeSpan.Zero || this.pendingTimeout < this.responseTimeout) throw new ArgumentOutOfRangeException(nameof(responseTimeout));
    }
    public void Initialize()
    {
        if (disposed || initialized || faulted) throw new TransportException("Open a fresh bench channel for each connection.");
        try
        {
            var info = channel.Open();
            if (!info.Api.StartsWith("04.04", StringComparison.Ordinal)) throw new TransportException("J2534 04.04 is required.");
            channel.Connect(500000, 0x7E0, 0x7E8);
            Adapter = $"{channel.AdapterName} · {info.Firmware} · {info.Driver} · {info.Api}"; initialized = true;
        }
        catch { faulted = true; Dispose(); throw; }
    }
    public byte[] Exchange(byte[] request)
    {
        BenchRequestPermit.CheckRequest(request);
        if (!initialized || disposed || faulted) throw new TransportException("Reconnect through a new bench operation. No retry is allowed.");
        request = request.ToArray();
        try
        {
            channel.ClearReceive(); permit.Prepare(request); channel.Write(request); permit.Clear();
            var clock = Stopwatch.StartNew(); var limit = responseTimeout; int pending = 0;
            while (clock.Elapsed < limit)
            {
                var frame = channel.Read(Math.Max(1, Math.Min(25, (int)(limit - clock.Elapsed).TotalMilliseconds)));
                if (frame is null || (frame.Status & 0x0B) != 0 || frame.Id != 0x7E8) continue;
                if ((frame.Status & 0x10) != 0 || frame.Data.Length is < 1 or > 4095) throw new TransportException("Malformed ISO-TP response.");
                var response = frame.Data;
                if (response[0] == 0x7F)
                {
                    if (response.Length != 3 || response[1] != request[0]) throw new TransportException("Unrelated or malformed negative reply.");
                    if (response[2] != 0x78) throw new NegativeResponseException(response[1], response[2]);
                    if (++pending > 256) throw new TransportException("Too many pending responses; request was not repeated.");
                    limit = pendingTimeout; continue;
                }
                if (response[0] != request[0] + 0x40) throw new TransportException("Unrelated ECU reply. Request was not repeated.");
                return response;
            }
            throw new TransportException("Bench request timed out. Acceptance may be uncertain; request was not repeated.");
        }
        catch (NegativeResponseException error) when (error.Code == 0x11 && request[0] is 0x09 or 0x01 or 0x23) { throw; }
        catch { faulted = true; permit.Clear(); throw; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; initialized = false; permit.Clear(); channel.Dispose();
    }
}
