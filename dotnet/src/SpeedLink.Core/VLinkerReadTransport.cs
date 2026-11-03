using System.Diagnostics;
using System.Text;

namespace SpeedLink.Core;

public interface ISerialConnection : IDisposable
{
    bool IsOpen
    {
        get;
    }
    int BaudRate
    {
        get; set;
    }
    int BytesToRead
    {
        get;
    }
    int Read(byte[] buffer, int offset, int count);
    int Write(byte[] data);
    void Flush();
    void DiscardInput();
}
public interface IMonotonicClock
{
    TimeSpan Elapsed
    {
        get;
    }
    void Sleep(int milliseconds);
}
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly Stopwatch timer = Stopwatch.StartNew();
    public TimeSpan Elapsed => timer.Elapsed;
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}
public class ElmTransport : IReadTransport
{
    protected readonly ISerialConnection Serial;
    protected readonly IMonotonicClock Clock;
    protected readonly IDiagnosticSink? diagnostics;
    public string Mode => "hardware";
    public string Adapter { get; protected set; } = "";
    public virtual int ReadChunkSize => 256;
    public bool ReadAttempted
    {
        get; set;
    }
    public bool Ready
    {
        get; protected set;
    }
    public bool Failed
    {
        get; protected set;
    }
    public ElmTransport(ISerialConnection serial, IMonotonicClock? clock = null, IDiagnosticSink? diagnostics = null)
    {
        Serial = serial;
        Clock = clock ?? new MonotonicClock();
        this.diagnostics = diagnostics;
    }
    protected void WriteExact(byte[] data)
    {
        if (Serial.Write(data) != data.Length)
            throw new TransportException("Incomplete serial write.");
        Serial.Flush();
    }
    protected string Prompt(double seconds, int maxBytes = 32768)
    {
        using var response = new MemoryStream();
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(seconds);
        var buffer = new byte[16384];
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            response.Write(buffer, 0, count);
            if (response.Length > maxBytes)
                throw new TransportException("Adapter response exceeded the receive limit.");
            var raw = response.GetBuffer().AsSpan(0, (int)response.Length);
            var at = raw.IndexOf((byte)'>');
            if (at >= 0)
            {
                if (raw[(at + 1)..].ToArray().Any(c => !char.IsWhiteSpace((char)c)))
                    throw new TransportException("Unexpected data after adapter prompt.");
                if (raw[..at].ToArray().Any(c => c > 127))
                    throw new TransportException("Non-ASCII adapter reply.");
                return Encoding.ASCII.GetString(raw[..at]);
            }
        }
        throw new TransportException("Adapter timeout. Command was sent once; reconnect before continuing.");
    }
    public string Command(string command, double timeout = 4)
    {
        if (Failed)
            throw new TransportException("Connection is no longer synchronized. Disconnect and reconnect.");
        bool control = command.StartsWith("AT", StringComparison.Ordinal) || command.StartsWith("ST", StringComparison.Ordinal) || command == "VTI";
        var started = Clock.Elapsed;
        if (control) diagnostics?.Record("serial.command_started", new { command, timeoutMs = timeout * 1000 });
        try
        {
            WriteExact(Encoding.ASCII.GetBytes(command + "\r"));
            string response = Prompt(timeout);
            if (control) diagnostics?.Record("serial.command_completed", new { command, response = response.Length <= 256 ? response : response[..256], durationMs = (Clock.Elapsed - started).TotalMilliseconds });
            else diagnostics?.Record("serial.reply", new { command, responseCharacters = response.Length,
                response = command.StartsWith("09", StringComparison.Ordinal) || command.StartsWith("23", StringComparison.Ordinal) ? "[payload omitted]" : response[..Math.Min(512, response.Length)],
                durationMs = (Clock.Elapsed - started).TotalMilliseconds });
            return response;
        }
        catch (Exception error) { if (control) diagnostics?.Record("serial.command_error", new { command, error = error.Message, durationMs = (Clock.Elapsed - started).TotalMilliseconds }); Failed = true; throw; }
    }
    public virtual void Initialize()
    {
        Command("ATZ", 5);
        Adapter = Command("ATI").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (Adapter.Length == 0 || Adapter.Contains('?'))
            throw new TransportException("Adapter identification failed.");
        foreach (var command in new[] { "ATE0", "ATL0", "ATS0", "ATH0", "ATSP6", "ATCAF1", "ATCFC1", "ATSH7E0", "ATCRA7E8", "ATAT0", "ATSTFF" })
        {
            var lines = Command(command).Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x != command).ToArray();
            if (lines.Length != 1 || lines[0].ToUpperInvariant() != "OK")
            {
                Failed = true;
                throw new TransportException(command + " was not accepted.");
            }
        }
        Ready = true;
    }
    public virtual byte[] Exchange(byte[] request)
    {
        DiagnosticProtocol.CheckReadOnly(request);
        if (!Ready)
            throw new TransportException("Initialize the adapter first.");
        try
        {
            var command = Convert.ToHexString(request);
            return DiagnosticProtocol.FinalResponse(request, DiagnosticProtocol.ParseElm(Command(command, 5), command));
        }
        catch (NegativeResponseException) { throw; }
        catch { Failed = true; throw; }
    }
    public virtual byte[] ExchangeLogger(byte[] request, int expectedResponseLength) => Exchange(request);
    public virtual void Dispose()
    {
        Ready = false;
        Serial.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class VLinkerReadTransport : ElmTransport
{
    private string serialIdentity = "";
    // Host USB-UART rate. ATSP6 configures the separate 500 kbit/s CAN link.
    public const int TransferBaud = 2000000;
    public int ConfiguredTransferBaud
    {
        get;
    }
    public override int ReadChunkSize => 3968;
    public int Baud => Serial.BaudRate;
    public string? CloseWarning
    {
        get; private set;
    }
    public VLinkerReadTransport(ISerialConnection serial, IMonotonicClock? clock = null, int transferBaud = TransferBaud, IDiagnosticSink? diagnostics = null) : base(serial, clock, diagnostics)
    {
        if (transferBaud is not (115200 or 230400 or 500000 or 1000000 or 2000000))
            throw new ArgumentOutOfRangeException(nameof(transferBaud), "Select a supported serial transfer rate.");
        ConfiguredTransferBaud = transferBaud;
    }
    public override void Initialize()
    {
        try
        {
            Serial.DiscardInput();
            Command("ATI");
            Command("ATE0");
            serialIdentity = Command("ATI").Trim();
            if (serialIdentity.Length == 0 || serialIdentity.Contains('?')) throw new TransportException("Adapter identity was not established.");
            var firmware = Command("VTI").Trim();
            if (!firmware.StartsWith("vLinker FS ", StringComparison.Ordinal))
                throw new TransportException("SpeedLink requires a Vgate vLinker FS.");
            foreach (var command in new[] { "ATL0", "ATS0", "ATH0", "ATSP6", "ATCAF1", "ATCFC1", "ATSH7E0", "ATCRA7E8", "ATAT0", "ATST32", "ATFCSM0", "STCSEGT1", "STCSEGR1" })
                if (Command(command).Trim() != "OK")
                    throw new TransportException(command + " was not accepted.");
            SetBaud(ConfiguredTransferBaud);
            Adapter = firmware + " · segmented read";
            Ready = true;
            diagnostics?.Record("adapter.ready", new { adapter = Adapter, serialBaud = Serial.BaudRate, canBitrate = 500000, readChunkBytes = ReadChunkSize, backend = "FTDI" });
        }
        catch { Failed = true; Ready = false; throw; }
    }
    private void SetBaud(int baud)
    {
        if (baud == Serial.BaudRate)
            return;
        diagnostics?.Record("serial.baud_change", new { previousBaud = Serial.BaudRate, requestedBaud = baud });
        if (Command("STBRT100").Trim() != "OK")
            throw new TransportException("Adapter rejected baud-switch timeout.");
        WriteExact(Encoding.ASCII.GetBytes("STSBR " + baud + "\r"));
        using var response = new MemoryStream();
        var buffer = new byte[4096];
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(2);
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            response.Write(buffer, 0, count);
            var text = Encoding.ASCII.GetString(response.GetBuffer(), 0, (int)response.Length);
            if (text.Contains("OK", StringComparison.Ordinal) || text.Contains('?'))
                break;
        }
        if (!Encoding.ASCII.GetString(response.ToArray()).Contains("OK", StringComparison.Ordinal))
            throw new TransportException("Adapter rejected temporary baud switch.");
        Serial.BaudRate = baud;
        Clock.Sleep(250);
        Serial.DiscardInput();
        WriteExact("STI\r"u8.ToArray());
        // The baud transition can emit an extra adapter prompt before STI.
        // Diagnostic exchanges retain the strict single-prompt parser above.
        using var identity = new MemoryStream();
        deadline = Clock.Elapsed + TimeSpan.FromSeconds(3);
        while (Clock.Elapsed < deadline)
        {
            var count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
            identity.Write(buffer, 0, count);
            if (identity.Length > 4096)
                throw new TransportException("Baud identity reply exceeded the receive limit.");
            var raw = identity.ToArray();
            if (raw.Any(value => value > 127))
                throw new TransportException("Non-ASCII baud identity reply.");
            var reply = Encoding.ASCII.GetString(raw);
            if (reply.Contains("STN", StringComparison.Ordinal) && reply.TrimEnd().EndsWith('>'))
            {
                var lines = reply.Replace('>', '\r').Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Count(line => line.StartsWith("STN", StringComparison.Ordinal)) != 1 || lines.Any(line => line != "STI" && !line.StartsWith("STN", StringComparison.Ordinal)))
                    throw new TransportException("Unexpected baud identity content.");
                return;
            }
        }
        throw new TransportException("Baud switch did not return a synchronized identity reply.");
    }
    public override byte[] Exchange(byte[] request)
    {
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        if (!Ready || Failed)
            throw new TransportException("Initialize a fresh connection before reading.");
        try
        {
            WriteExact(Encoding.ASCII.GetBytes($"STPX L:{request.Length},T:200,R:1\r"));
            if (Prompt(3, 65536).Trim().ToUpperInvariant() != "DATA")
                throw new TransportException("Adapter did not request the read payload.");
            WriteExact(Encoding.ASCII.GetBytes(Convert.ToHexString(request) + "\r"));
            return DiagnosticProtocol.FinalResponse(request, DiagnosticProtocol.ParseSegmented(Prompt(6, 65536)));
        }
        catch (NegativeResponseException) { throw; } // A complete NRC reply retains framing synchronization.
        catch { Failed = true; throw; }
    }
    public override byte[] ExchangeLogger(byte[] request, int expectedResponseLength)
    {
        DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
        bool standard = Mode01LoggerProtocol.IsAllowed(request);
        if ((!standard && (request.Length != 3 || request[0] != 0x22)) || expectedResponseLength is < 3 or > 4095 ||
            !standard && expectedResponseLength < 4 ||
            standard && expectedResponseLength != Mode01LoggerProtocol.ResponseLength(request))
            throw new TransportException("Invalid logger request or reply length.");
        if (!Ready || Failed) throw new TransportException("Initialize a fresh connection before logging.");
        try
        {
            WriteExact(Encoding.ASCII.GetBytes($"STPX L:{request.Length},T:200,R:1\r"));
            if (Prompt(3, 65536).Trim().ToUpperInvariant() != "DATA") throw new TransportException("Adapter did not request the logger payload.");
            WriteExact(Encoding.ASCII.GetBytes(Convert.ToHexString(request) + "\r"));
            var started = Clock.Elapsed;
            using var received = new MemoryStream(); var buffer = new byte[16384];
            while (Clock.Elapsed - started < TimeSpan.FromSeconds(6))
            {
                int count = Serial.Read(buffer, 0, Math.Max(1, Math.Min(Serial.BytesToRead, buffer.Length)));
                received.Write(buffer, 0, count);
                if (received.Length > 32768) throw new TransportException("Logger response exceeded the receive limit.");
                var bytes = received.GetBuffer().AsSpan(0, (int)received.Length);
                if (bytes.ToArray().Any(b => b > 127)) throw new TransportException("Non-ASCII logger reply.");
                int prompt = bytes.IndexOf((byte)'>');
                if (prompt >= 0)
                {
                    if (bytes[(prompt + 1)..].ToArray().Any(b => !char.IsWhiteSpace((char)b))) throw new TransportException("Unexpected data after adapter prompt.");
                    return ValidateLoggerReply(request, expectedResponseLength, DiagnosticProtocol.ParseSegmented(Encoding.ASCII.GetString(bytes[..prompt])));
                }
                // Segmented replies are emitted as complete hex lines. Do not infer completion from a byte prefix,
                // a pending response, an unchanged sensor value, or a partially reassembled message.
                if (bytes.Length == 0 || bytes[^1] is not (13 or 10)) continue;
                string text = Encoding.ASCII.GetString(bytes);
                string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Length == 0 || lines.Any(line => line.Length % 2 != 0 || line.Any(c => !Uri.IsHexDigit(c)))) continue;
                var packets = DiagnosticProtocol.ParseSegmented(text);
                if (packets.All(p => p.AsSpan().SequenceEqual(new byte[] { 0x7F, request[0], 0x78 })))
                {
                    if (packets.Count > 32) throw new TransportException("Too many pending logger replies.");
                    continue;
                }
                NegativeResponseException? negative = null; byte[]? reply = null;
                try { reply = ValidateLoggerReply(request, expectedResponseLength, packets); }
                catch (NegativeResponseException error) { negative = error; }
                // '!' cannot repeat a diagnostic request if the natural prompt races this write. A CR can.
                // Interrupt only the idle receive tail, then consume its prompt and verify adapter-only identity.
                WriteExact("!"u8.ToArray());
                string footer = Prompt(3, 4096).Trim();
                if (footer is not ("" or "STOPPED")) throw new TransportException("Unexpected logger completion footer.");
                string adapter = Command("ATI", 3).Trim();
                if (adapter == "?") adapter = Command("ATI", 3).Trim(); // A raced prompt buffered '!'; this retries only adapter identification.
                if (adapter != serialIdentity)
                    throw new TransportException("Logger completion did not restore a synchronized adapter prompt.");
                diagnostics?.Record("serial.logger_completed", new { expectedResponseLength, elapsedMs = (Clock.Elapsed - started).TotalMilliseconds, idleWaitEnded = true });
                if (negative is not null) throw negative;
                return reply!;
            }
            throw new TransportException("Logger response timed out. Request was not resent; reconnect before continuing.");
        }
        catch (NegativeResponseException) { throw; }
        catch { Failed = true; throw; }
    }
    private static byte[] ValidateLoggerReply(byte[] request, int expectedLength, IReadOnlyList<byte[]> packets)
    {
        var reply = DiagnosticProtocol.FinalResponse(request, packets);
        if (Mode01LoggerProtocol.IsAllowed(request))
        {
            if (reply.Length != expectedLength) throw new TransportException("Unexpected standard logger reply length.");
            Mode01LoggerProtocol.Split(request, reply); return reply;
        }
        if (reply.Length != expectedLength || reply[0] != 0x62 || !reply.AsSpan(1, 2).SequenceEqual(request.AsSpan(1, 2)))
            throw new TransportException("Unexpected logger response length or identifier.");
        return reply;
    }
    public override void Dispose()
    {
        try
        {
            if (Serial.IsOpen && !Failed)
                try
                {
                    SetBaud(115200);
                }
                catch (Exception error) { CloseWarning = error.Message; }
        }
        finally { base.Dispose(); }
    }
}
