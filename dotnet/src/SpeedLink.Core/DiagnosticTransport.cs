using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace SpeedLink.Core;

public sealed class DiagnosticTransport(IReadTransport inner, DiagnosticSession session) : IReadTransport
{
    public IReadTransport Inner => inner;
    public string Mode => inner.Mode;
    public string Adapter => inner.Adapter;
    public int ReadChunkSize => inner.ReadChunkSize;
    public bool ReadAttempted { get => inner.ReadAttempted; set => inner.ReadAttempted = value; }
    public IDiagnosticSink Diagnostics => session;
    public static string Describe(ReadOnlySpan<byte> request)
    {
        if (request.SequenceEqual(new byte[] { 9, 2 })) return "Read VIN / chassis ID";
        if (request.SequenceEqual(new byte[] { 9, 4 })) return "Read calibration";
        if (request.SequenceEqual(new byte[] { 1, 12 })) return "Check engine speed";
        if (request.SequenceEqual(new byte[] { 0x10, 0x87 }) || request.SequenceEqual(new byte[] { 0x10, 3 })) return "Enter ROM read session";
        if (request.SequenceEqual(new byte[] { 0x27, 1 }) || request.SequenceEqual(new byte[] { 0x27, 3 })) return "Request security seed";
        if (request.Length >= 2 && request[0] == 0x27 && request[1] is 2 or 4) return "Submit security key";
        if (request.Length == 7 && request[0] == 0x23) return $"Read ROM at 0x{BinaryPrimitives.ReadUInt32BigEndian(request[1..]):X6} ({BinaryPrimitives.ReadUInt16BigEndian(request[5..])} bytes)";
        if (request.Length == 8 && request[0] == 0x23 && request[1] == 0x24) return $"Read Gen 2 ROM at 0x{BinaryPrimitives.ReadUInt32BigEndian(request[2..]):X6} ({BinaryPrimitives.ReadUInt16BigEndian(request[6..])} bytes)";
        return request.Length == 3 && request[0] == 0x22 ? "Read logger identifier " + Convert.ToHexString(request[1..]) : "Diagnostic service " + (request.IsEmpty ? "empty" : request[0].ToString("X2"));
    }
    public byte[] Exchange(byte[] request)
        => ExchangeCore(request, () => inner.Exchange(request));
    public byte[] ExchangeLogger(byte[] request, int expectedResponseLength)
        => ExchangeCore(request, () => inner.ExchangeLogger(request, expectedResponseLength));
    private byte[] ExchangeCore(byte[] request, Func<byte[]> exchange)
    {
        string stage = Describe(request); var timer = Stopwatch.StartNew();
        string requestHex = Convert.ToHexString(request);
        session.Record("exchange.started", new { stage, requestHex, requestBytes = request.Length });
        try
        {
            var response = exchange();
            if (request.AsSpan().SequenceEqual(new byte[] { 9, 2 }) && response.Length > 3) session.RegisterSecret(Encoding.ASCII.GetString(response.AsSpan(3).TrimEnd((byte)0)));
            bool security = request.Length > 0 && request[0] == 0x27;
            bool omit = request.Length > 0 && request[0] is 0x09 or 0x23 || response.Length > 64 && !security || response.Length > 0 && response[0] == 0x63;
            session.Record("exchange.completed", new { stage, durationMs = timer.Elapsed.TotalMilliseconds, responseBytes = response.Length,
                responseHex = omit ? Convert.ToHexString(response.AsSpan(0, Math.Min(response.Length > 0 && response[0] == 0x63 ? 1 : 2, response.Length))) + " [payload omitted]" : Convert.ToHexString(response),
                responseSha256 = request.Length > 0 && request[0] == 0x23 ? RomInspector.Hash(response) : null });
            return response;
        }
        catch (NegativeResponseException error) { session.Record("exchange.negative_response", new { stage, durationMs = timer.Elapsed.TotalMilliseconds, service = error.Service.ToString("X2"), negativeResponse = error.Code.ToString("X2"), responseHex = $"7F{error.Service:X2}{error.Code:X2}", error = error.Message }); throw; }
        catch (Exception error)
        {
            session.Record("exchange.error", new { stage, durationMs = timer.Elapsed.TotalMilliseconds, error = error.Message, type = error.GetType().FullName, stack = new StackTrace(error, false).ToString(), requestSentAgain = false });
            if (error is TransportException) throw new TransportException(stage + ": " + error.Message, error);
            throw;
        }
    }
    public void Dispose() { try { inner.Dispose(); } finally { session.Record("connection.closed"); } }
}
