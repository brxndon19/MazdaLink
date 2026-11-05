// Uses the vLinker FS programming exchange without expanding its reader allowlist.
using System.Globalization;
using System.Text;

namespace SpeedLink.Core;

public sealed class VLinkerBenchFlashTransport(ISerialConnection serial, IMonotonicClock? clock = null) : IBenchFlashTransport
{
    private readonly VLinkerReadTransport link = new(serial, clock);
    public string Mode => link.Mode;
    public string Adapter => link.Adapter + " · Gen 1 programming";
    public int ReadChunkSize => link.ReadChunkSize;
    public bool ReadAttempted { get => link.ReadAttempted; set => link.ReadAttempted = value; }
    public string? CloseWarning => link.CloseWarning;
    public int BatteryMillivolts
    {
        get
        {
            if (!link.Ready || link.Failed) throw new TransportException("Reconnect before checking supply.");
            string reply = link.Command("ATRV").Trim();
            if (!reply.EndsWith('V') || !decimal.TryParse(reply.AsSpan(0, reply.Length - 1), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal volts) || volts is <= 0 or > 30)
                throw new TransportException("vLinker did not report a valid supply voltage.");
            decimal millivolts = volts * 1000m;
            if (millivolts != decimal.Truncate(millivolts))
                throw new TransportException("vLinker reported an unexpected supply voltage format.");
            return (int)millivolts;
        }
    }

    public void Initialize()
    {
        if (!serial.IsOpen || link.Ready || link.Failed) throw new TransportException("Open a fresh bench channel for each connection.");
        link.InitializeBench();
    }

    public byte[] Exchange(byte[] request) => link.ExchangeBench(request);
    public void Dispose() => link.Dispose();
}

public sealed partial class VLinkerReadTransport
{
    internal void InitializeBench()
    {
        Initialize();
        try
        {
            if (Command("ATR1").Trim() != "OK") throw new TransportException("vLinker did not enable diagnostic responses.");
        }
        catch { Failed = true; throw; }
    }

    internal byte[] ExchangeBench(byte[] request)
    {
        BenchRequestPermit.CheckRequest(request);
        if (request[0] is 0x09 or 0x01 or 0x23)
            return ExchangeRead(request, 12);
        if (!Ready || Failed)
            throw new TransportException("Reconnect through a new bench operation. No retry is allowed.");

        try
        {
            // R:1 would end the exchange on a response-pending packet. Keep listening
            // for the ECU's final reply and only end the idle receive tail after it.
            int adapterTimeoutMs = request[0] == 0xB1 ? 60000 : 10000;
            WriteExact(Encoding.ASCII.GetBytes(FormattableString.Invariant($"STPX L:{request.Length},T:{adapterTimeoutMs}\r")));
            if (!Prompt(3, 4096).Trim().Equals("DATA", StringComparison.OrdinalIgnoreCase))
                throw new TransportException("vLinker did not request the bench payload.");
            WriteExact(Encoding.ASCII.GetBytes(Convert.ToHexString(request) + "\r"));
            return ReceiveBenchReply(request, TimeSpan.FromMilliseconds(adapterTimeoutMs + 5000));
        }
        catch { Failed = true; throw; }
    }

    private byte[] ReceiveBenchReply(byte[] request, TimeSpan timeout)
    {
        using var received = new MemoryStream();
        var buffer = new byte[4096];
        var deadline = Clock.Elapsed + timeout;
        while (Clock.Elapsed < deadline)
        {
            int count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            if (count == 0) continue;
            received.Write(buffer, 0, count);
            if (received.Length > 8192) throw new TransportException("Bench reply exceeded the receive limit.");
            var bytes = received.GetBuffer().AsSpan(0, (int)received.Length);
            if (bytes.IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0) throw new TransportException("Non-ASCII bench reply.");
            int prompt = bytes.IndexOf((byte)'>');
            if (prompt >= 0 && !Encoding.ASCII.GetString(bytes[(prompt + 1)..]).All(char.IsWhiteSpace))
                throw new TransportException("Unexpected data after adapter prompt.");
            if (prompt < 0 && bytes[^1] is not (13 or 10)) continue;

            string[] lines = Encoding.ASCII.GetString(prompt < 0 ? bytes : bytes[..prompt])
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var packets = new List<byte[]>(lines.Length);
            foreach (string line in lines)
            {
                string hex = DiagnosticProtocol.Compact(line);
                if (hex.Length is 0 or > 32 || hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
                    throw new TransportException("vLinker returned a malformed bench reply.");
                packets.Add(Convert.FromHexString(hex));
            }
            if (packets.Count > 257) throw new TransportException("Too many bench replies. Request was not repeated.");
            if (prompt < 0 && packets.All(p => p.AsSpan().SequenceEqual(new byte[] { 0x7F, request[0], 0x78 }))) continue;
            var final = DiagnosticProtocol.FinalResponse(request, packets);
            if (prompt < 0) FinishReceiveTail();
            return final;
        }
        throw new TransportException("vLinker bench reply timed out. Acceptance may be uncertain. Request was not repeated.");
    }
}
