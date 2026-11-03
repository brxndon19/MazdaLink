using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpeedLink.Core;

public class TransportException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class NegativeResponseException(byte service, byte code)
    : TransportException($"ECU rejected service {service:X2}: NRC {code:X2}.")
{
    public byte Service { get; } = service; public byte Code { get; } = code;
}

public static partial class DiagnosticProtocol
{
    [GeneratedRegex("^[0-9A-F]{3}$")] private static partial Regex LengthLine();
    [GeneratedRegex("^([0-9A-F]):([0-9A-F]+)$")] private static partial Regex IndexedLine();
    [GeneratedRegex("^(?:[0-9A-F]{2}){1,7}$")] private static partial Regex SingleLine();
    public static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    public static IReadOnlyList<byte[]> ParseElm(string text, string command = "")
    {
        var packets = new List<byte[]>();
        var current = new List<byte>();
        int? length = null;
        int index = 0;
        var lines = text.Replace('>', '\r').Replace('\n', '\r').Split('\r');
        var faults = new HashSet<string> { "BUFFERFULL", "CANERROR", "BUSDATAERROR", "STOPPED", "UNABLETOCONNECT", "NODATA" };
        foreach (var raw in lines)
            if (faults.Contains(Compact(raw)))
                throw new TransportException("Adapter reported: " + raw.Trim()[..Math.Min(120, raw.Trim().Length)]);
        foreach (var raw in lines)
        {
            var line = Compact(raw);
            if (line.Length == 0 || line == Compact(command) || line == "SEARCHING...")
                continue;
            if (length is null && LengthLine().IsMatch(line))
            {
                length = int.Parse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (length is < 8 or > 4095)
                    throw new TransportException("Invalid ELM multiline length.");
                current.Clear();
                index = 0;
                continue;
            }
            if (line.Contains(':'))
            {
                var match = IndexedLine().Match(line);
                if (length is null || !match.Success || int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != index % 16)
                    throw new TransportException("Missing or out-of-order ELM response line.");
                var hex = match.Groups[2].Value;
                if (hex.Length % 2 != 0 || hex.Length is < 2 or > 14)
                    throw new TransportException("Malformed ELM response bytes.");
                current.AddRange(Convert.FromHexString(hex));
                index++;
                if (current.Count >= length.Value)
                {
                    packets.Add(current.Take(length.Value).ToArray());
                    length = null;
                    current.Clear();
                }
                continue;
            }
            if (length is not null)
                throw new TransportException("Incomplete ELM multiline response.");
            if (!SingleLine().IsMatch(line))
                throw new TransportException("Adapter reported: " + raw.Trim()[..Math.Min(120, raw.Trim().Length)]);
            packets.Add(Convert.FromHexString(line));
        }
        if (length is not null)
            throw new TransportException("Truncated ELM multiline response.");
        if (packets.Count == 0)
            throw new TransportException("Adapter returned no ECU response.");
        return packets;
    }
    public static IReadOnlyList<byte[]> ParseSegmented(string text)
    {
        if (Compact(text).Contains("<DATAERROR", StringComparison.Ordinal))
            throw new TransportException("Adapter reported: DATA ERROR (partial response).");
        var lines = text.Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries).Select(Compact).Where(x => x.Length > 0).ToArray();
        if (lines.Length > 0 && lines.All(s => s.Length % 2 == 0 && s.All(c => "0123456789ABCDEF".Contains(c))))
        {
            var packets = lines.Select(Convert.FromHexString).ToArray();
            if (packets.Any(p => p.Length is 0 or > 4095))
                throw new TransportException("Invalid segmented response size.");
            return packets;
        }
        return ParseElm(text);
    }
    public static byte[] FinalResponse(ReadOnlySpan<byte> request, IReadOnlyList<byte[]> packets)
    {
        if (request.IsEmpty)
            throw new TransportException("Empty request.");
        byte[]? final = null;
        foreach (var packet in packets)
        {
            if (packet.Length == 0)
                throw new TransportException("Empty ECU response.");
            if (packet[0] == 0x7F)
            {
                if (packet.Length != 3 || packet[1] != request[0] || final is not null)
                    throw new TransportException("Malformed or unexpected negative response.");
                if (packet[2] == 0x78)
                    continue;
                throw new NegativeResponseException(packet[1], packet[2]);
            }
            if (final is not null || packet[0] != request[0] + 0x40)
                throw new TransportException("Unexpected or multiple final ECU responses.");
            final = packet;
        }
        return final ?? throw new TransportException("Response remained pending. Request was not resent; reconnect before continuing.");
    }
    public static void CheckReadOnly(ReadOnlySpan<byte> request, int maxRead = 256)
    {
        bool allowed = request.SequenceEqual(new byte[] { 0x09, 0x02 }) || request.SequenceEqual(new byte[] { 0x09, 0x04 }) || request.SequenceEqual(new byte[] { 0x01, 0x0C })
            || request.SequenceEqual(new byte[] { 0x10, 0x87 }) || request.SequenceEqual(new byte[] { 0x27, 0x01 }) || (request.Length == 5 && request[0] == 0x27 && request[1] == 0x02);
        allowed |= Mode01LoggerProtocol.IsAllowed(request);
        allowed |= request.SequenceEqual(new byte[] { 0x10, 0x03 }) || request.SequenceEqual(new byte[] { 0x27, 0x03 }) ||
            (request.Length == 5 && request[0] == 0x27 && request[1] == 0x04);
        allowed |= request.Length == 3 && request[0] == 0x22 && LoggerDefinition.AllowsKnownReadRequest(request);
        if (request.Length == 7 && request[0] == 0x23)
        {
            var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(request.Slice(1, 4));
            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.Slice(5, 2));
            allowed |= count > 0 && count <= maxRead && (ulong)address + count <= RomInspector.RomSize;
        }
        if (request.Length == 8 && request[0] == 0x23 && request[1] == 0x24)
        {
            var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(request.Slice(2, 4));
            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.Slice(6, 2));
            allowed |= count > 0 && count <= Math.Min(maxRead, 1023) && (ulong)address + count <= RomInspector.RomSize;
        }
        if (!allowed)
            throw new TransportException("Live programming is disabled. Request blocked before serial I/O.");
    }
}

public interface IReadTransport : IDisposable
{
    // Only the logger supplies a definition-verified reply length. Other operations retain their transport policy.
    byte[] ExchangeLogger(byte[] request, int expectedResponseLength) => Exchange(request);
    IDiagnosticSink? Diagnostics => null;
    string Mode
    {
        get;
    }
    string Adapter
    {
        get;
    }
    int ReadChunkSize
    {
        get;
    }
    bool ReadAttempted
    {
        get; set;
    }
    byte[] Exchange(byte[] request);
}

public sealed record EcuIdentity(string Mode, string Adapter, string? Vin, string? Calibration,
    string? VinStatus = null, string? CalibrationStatus = null, string? ReadProtocolName = null)
{
    public string? ChassisId { get; init; }
    public string? VehicleIdentifier => Vin ?? ChassisId;
    public string VehicleIdentifierLabel => ChassisId is null ? "VIN" : "Chassis ID";
    public bool MatchesVehicle(EcuIdentity other) => Vin == other.Vin && ChassisId == other.ChassisId && Calibration == other.Calibration;
    public object ToManifest() => new Dictionary<string, object?>
    {
        ["mode"] = Mode,
        ["adapter"] = Adapter,
        ["vin"] = Vin,
        ["chassisId"] = ChassisId,
        ["calibration"] = Calibration,
        ["vinStatus"] = VinStatus,
        ["calibrationStatus"] = CalibrationStatus,
        ["readProtocol"] = ReadProtocolName,
        ["requestID"] = "7E0",
        ["responseID"] = "7E8",
        ["bitrate"] = 500000,
        ["vehicleMatchVerified"] = false,
        ["capturedBeforeReadSession"] = true
    };
}
