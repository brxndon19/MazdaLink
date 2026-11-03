using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpeedLink.Core;

public readonly record struct LoggerSample(double Value, DateTimeOffset Timestamp, bool IsValid = true);
public sealed record LoggerChannel(string Id, string Name, string Group, string Unit, string Help,
    string? Query, IReadOnlyList<string> Inputs, IReadOnlyList<string> DisplayUnits);
public sealed record UnavailableLoggerChannel(string Id, string Name, string Reason);

public sealed class LoggerCalibration
{
    public IReadOnlyDictionary<string, double> Values { get; }
    public string? SourceRomSha256 { get; }

    internal LoggerCalibration(IDictionary<string, double> values, string? sourceRomSha256)
    {
        if (!values.TryGetValue("map_gain_kpa_per_v", out double gain) || !double.IsFinite(gain) || gain <= 0 ||
            !values.TryGetValue("map_offset_kpa", out double offset) || !double.IsFinite(offset))
            throw new InvalidDataException("MAP calibration needs a positive finite gain and a finite offset.");
        Values = new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(values, StringComparer.Ordinal));
        SourceRomSha256 = sourceRomSha256;
    }

    public static LoggerCalibration ExplicitMap(double gainKpaPerVolt, double subtractKpa) => new(
        new Dictionary<string, double> { ["map_gain_kpa_per_v"] = gainKpaPerVolt, ["map_offset_kpa"] = subtractKpa }, null);
}

/// <summary>Offline definition reader and decoder. This class performs no adapter or ECU operations.</summary>
public sealed class LoggerDefinition
{
    private static readonly Lazy<IReadOnlyList<LoggerDefinition>> bundled = new(() =>
    {
        var assembly = typeof(LoggerDefinition).Assembly;
        var profiles = new List<LoggerDefinition>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("SpeedLink.Core.Logging.", StringComparison.Ordinal) && n.EndsWith(".logger.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var file = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Bundled logger profile is missing.");
            using var memory = new MemoryStream(); file.CopyTo(memory); profiles.Add(Parse(memory.ToArray()));
        }
        if (profiles.Count == 0 || profiles.SelectMany(p => p.SoftwareIds).Distinct(StringComparer.Ordinal).Count() != profiles.Sum(p => p.SoftwareIds.Count))
            throw new InvalidDataException("Logger catalog is empty or has duplicate software IDs.");
        return profiles.AsReadOnly();
    });
    public static IReadOnlyList<LoggerDefinition> Known => bundled.Value;
    public static LoggerDefinition Speed6 => Known.Single(p => p.SoftwareIds.Contains("L38KEH000", StringComparer.Ordinal));
    public static LoggerDefinition? ForCalibration(string? calibration)
    {
        var matches = Known.Where(p => p.MatchesCalibrationId(calibration)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public static (LoggerDefinition Definition, LoggerCalibration Calibration) BindKnownRom(ReadOnlySpan<byte> image, IReadOnlyList<LoggerDefinition>? definitions = null)
    {
        if (image.Length != 1_048_576) throw new InvalidDataException("Load a complete 1 MiB ECU image.");
        LoggerDefinition? match = null;
        foreach (var profile in definitions ?? Known)
        {
            if (!profile.HasSoftwareMarker(image)) continue;
            if (match is not null) throw new InvalidDataException("The image contains multiple logger software identities.");
            match = profile;
        }
        if (match is null) throw new InvalidDataException("No logger profile matches this ROM. ROM reading is still available.");
        return (match, match.BindRom(image));
    }
    internal static bool AllowsKnownReadRequest(ReadOnlySpan<byte> request)
    {
        foreach (var profile in Known) if (profile.AllowsReadRequest(request)) return true;
        return false;
    }
    internal bool AllowsReadRequest(ReadOnlySpan<byte> request) => request.Length == 3 && request[0] == 0x22 && queries.ContainsKey(Convert.ToHexString(request[1..]));
    private readonly JsonElement root;
    private readonly Dictionary<string, JsonElement> queries;
    private readonly Dictionary<string, JsonElement> channels;
    public IReadOnlyCollection<string> ChannelIds => channels.Keys;
    public IReadOnlyCollection<string> QueryIds => queries.Keys;
    public string Id => Text(root, "id");
    public string DefinitionVersion => Text(root, "definitionVersion");
    public string Vehicle => Text(root, "vehicle");
    public IReadOnlyList<UnavailableLoggerChannel> UnavailableChannels => root.TryGetProperty("unavailableChannels", out var unavailable) ?
        unavailable.EnumerateArray().Select(c => new UnavailableLoggerChannel(Text(c, "id"), Text(c, "name"), Text(c, "reason"))).ToArray() : [];
    public IReadOnlyList<string> SoftwareIds => root.GetProperty("softwareIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
    public IReadOnlyList<LoggerChannel> Channels => channels.Values.Select(c => new LoggerChannel(
        Text(c, "id"), Text(c, "name"), Text(c, "group"), Text(c, "unit"), Text(c, "help"),
        Text(c, "kind") == "query" ? Text(c, "query") : null,
        Text(c, "kind") == "calculated" ? c.GetProperty("calculation").GetProperty("inputs").EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
        new[] { Text(c, "unit") }.Concat(CommonUnits(Text(c, "unit"))).Concat(c.TryGetProperty("displayConversions", out var units) ?
            units.EnumerateArray().Select(x => Text(x, "unit")) : []).ToArray())).ToArray();
    private static string[] CommonUnits(string unit) => unit switch { "kPa" => ["psi", "bar"], "degC" => ["degF"], "km/h" => ["mph"], _ => [] };
    public bool MatchesCalibrationId(string? calibration) => calibration is not null && SoftwareIds.Any(id =>
        MatchesIdentityToken(calibration, id) || RomInspector.KnownProfiles.Any(profile =>
            profile.Calibration == id && MatchesIdentityToken(calibration, profile.Identity)));
    private static bool MatchesIdentityToken(string calibration, string identity) =>
        // ECUs can report the complete ROM identity, including its adjacent build ID.
        // Accept only known complete identities; an arbitrary suffix is not a match.
        System.Text.RegularExpressions.Regex.IsMatch(calibration, "(?<![A-Z0-9])" +
            System.Text.RegularExpressions.Regex.Escape(identity) + "(?![A-Z0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private LoggerDefinition(JsonElement element)
    {
        root = element.Clone();
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported logger definition version.");
        var protocol = root.GetProperty("protocol");
        if (Text(protocol, "transport") != "ISO15765" || protocol.GetProperty("canBitrate").GetInt32() != 500000 ||
            Text(protocol, "requestCanId") != "0x7E0" || Text(protocol, "responseCanId") != "0x7E8" ||
            Text(protocol, "service") != "0x22" || Text(protocol, "positiveService") != "0x62" ||
            Text(protocol, "byteOrder") != "big" || protocol.GetProperty("identifierBytes").GetInt32() != 2 ||
            !protocol.GetProperty("requireExactResponseLength").GetBoolean() || !protocol.GetProperty("requireIdentifierEcho").GetBoolean() ||
            protocol.GetProperty("sessionChanges").GetArrayLength() != 0 || Text(protocol, "sessionPolicy") != "normal_application_logging")
            throw new InvalidDataException("Logger definitions must use the bounded read-only identifier protocol.");
        queries = root.GetProperty("queries").EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        channels = root.GetProperty("channels").EnumerateArray().ToDictionary(p => Text(p, "id"), p => p, StringComparer.Ordinal);
        if (queries.Count is < 1 or > 128 || channels.Count is < 1 or > 256)
            throw new InvalidDataException("Unexpected logger definition size.");
        if (root.GetProperty("codeFingerprints").GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("Logger definitions need ROM code fingerprints.");
        foreach (var fingerprint in root.GetProperty("codeFingerprints").EnumerateArray())
        {
            string hash = Text(fingerprint, "sha256");
            if (hash.Length != 64 || hash.Any(c => !"0123456789abcdefABCDEF".Contains(c)) ||
                fingerprint.GetProperty("ranges").GetArrayLength() is < 1 or > 16)
                throw new InvalidDataException("Invalid logger code fingerprint.");
            foreach (var range in fingerprint.GetProperty("ranges").EnumerateArray())
            {
                int start = Hex(Text(range, "start")), length = range.GetProperty("length").GetInt32();
                if (length < 1 || start < 0 || start > 1_048_576 - length)
                    throw new InvalidDataException("Invalid logger code range.");
            }
        }
        foreach (var (id, query) in queries)
        {
            if (id.Length != 4 || id.Any(c => !"0123456789ABCDEF".Contains(c)) ||
                Text(query, "requestHex") != "22" + id || Text(query, "positiveHeaderHex") != "62" + id ||
                query.GetProperty("dataBytes").GetInt32() is not (1 or 2 or 4))
                throw new InvalidDataException("Invalid logger query.");
        }
        foreach (var channel in channels.Values)
        {
            if (Text(channel, "kind") == "query")
            {
                if (!queries.TryGetValue(Text(channel, "query"), out var query))
                    throw new InvalidDataException("Unknown logger query dependency.");
                var decode = channel.GetProperty("decode");
                int start = decode.GetProperty("byteOffset").GetInt32(), width = decode.GetProperty("byteLength").GetInt32();
                if (width is not (1 or 2 or 4) || start < 0 || start > query.GetProperty("dataBytes").GetInt32() - width)
                    throw new InvalidDataException("Logger field exceeds its payload.");
                if (Text(decode, "operation") == "bit")
                {
                    ulong mask = decode.GetProperty("mask").GetUInt64();
                    if (mask == 0 || (mask & (mask - 1)) != 0 || mask >= (1UL << (8 * width)) || decode.GetProperty("signed").GetBoolean())
                        throw new InvalidDataException("Invalid logger bit mask.");
                }
                else if (Text(decode, "operation") == "linear")
                {
                    Number(decode, "multiplier"); Number(decode, "offset");
                    if (decode.TryGetProperty("afterLinear", out var transform) &&
                        (Text(transform, "operation") != "calibrated_affine" || Text(transform, "gain") != "map_gain_kpa_per_v" ||
                         Text(transform, "subtract") != "map_offset_kpa"))
                        throw new InvalidDataException("Unsupported calibration operation.");
                }
                else throw new InvalidDataException("Unsupported logger decode operation.");
            }
            else if (Text(channel, "kind") == "calculated")
            {
                var calculation = channel.GetProperty("calculation");
                if (calculation.GetProperty("inputs").GetArrayLength() != 2 ||
                    !calculation.GetProperty("requireValidFreshInputs").GetBoolean())
                    throw new InvalidDataException("Invalid calculated logger inputs.");
                string operation = Text(calculation, "operation");
                if (operation != "subtract" && (operation != "product_divide" || Number(calculation, "divisor") <= 0))
                    throw new InvalidDataException("Unsupported logger calculation.");
            }
            else throw new InvalidDataException("Unsupported logger channel kind.");
        }
        var complete = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id, HashSet<string> active)
        {
            if (complete.Contains(id)) return;
            if (!channels.TryGetValue(id, out var channel) || !active.Add(id))
                throw new InvalidDataException("Unknown or circular logger dependency.");
            if (Text(channel, "kind") == "calculated")
                foreach (var input in channel.GetProperty("calculation").GetProperty("inputs").EnumerateArray())
                    Visit(input.GetString()!, active);
            active.Remove(id); complete.Add(id);
        }
        foreach (string id in channels.Keys) Visit(id, new HashSet<string>(StringComparer.Ordinal));
    }

    public static LoggerDefinition Load(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is < 2 or > 1_048_576) throw new InvalidDataException("Unexpected logger definition size.");
        byte[] data = new byte[(int)file.Length];
        file.ReadExactly(data);
        if (file.ReadByte() != -1) throw new InvalidDataException("Logger definition changed while reading.");
        return Parse(data);
    }

    public static LoggerDefinition Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length is < 2 or > 1_048_576) throw new InvalidDataException("Unexpected logger definition size.");
        using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 48 });
        void Unique(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.EnumerateObject())
                {
                    if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate logger definition property.");
                    Unique(property.Value);
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Unique(item);
        }
        Unique(document.RootElement);
        return new LoggerDefinition(document.RootElement);
    }

    public byte[] RequestFor(string channelId)
    {
        var channel = channels[channelId];
        if (Text(channel, "kind") != "query") throw new InvalidOperationException("Calculated channels have no ECU request.");
        return Convert.FromHexString(Text(queries[Text(channel, "query")], "requestHex"));
    }

    public int ResponseLengthFor(string channelId)
    {
        var channel = channels[channelId];
        if (Text(channel, "kind") != "query") throw new InvalidOperationException("Calculated channels have no ECU reply.");
        return 3 + queries[Text(channel, "query")].GetProperty("dataBytes").GetInt32();
    }

    public double Decode(string channelId, ReadOnlySpan<byte> response, LoggerCalibration? calibration = null)
    {
        var channel = channels[channelId];
        if (Text(channel, "kind") != "query") throw new InvalidOperationException("Use Calculate for calculated channels.");
        var query = queries[Text(channel, "query")];
        if (response.Length == 3 && response[0] == 0x7F && response[1] == 0x22)
            throw new NegativeResponseException(0x22, response[2]);
        byte[] header = Convert.FromHexString(Text(query, "positiveHeaderHex"));
        if (response.Length != 3 + query.GetProperty("dataBytes").GetInt32() || !response[..3].SequenceEqual(header))
            throw new TransportException("Unexpected logger response length or identifier.");
        var decode = channel.GetProperty("decode");
        int width = decode.GetProperty("byteLength").GetInt32(), start = 3 + decode.GetProperty("byteOffset").GetInt32();
        ulong raw = 0;
        foreach (byte value in response.Slice(start, width)) raw = (raw << 8) | value;
        if (Text(decode, "operation") == "bit") return (raw & decode.GetProperty("mask").GetUInt64()) != 0 ? 1 : 0;
        long signed = (long)raw;
        if (decode.GetProperty("signed").GetBoolean() && (raw & (1UL << (width * 8 - 1))) != 0) signed -= 1L << (width * 8);
        double result = signed * Number(decode, "multiplier") + Number(decode, "offset");
        if (decode.TryGetProperty("afterLinear", out var transform))
        {
            if (calibration is null) throw new InvalidOperationException("Load a matching ECU backup or explicitly select the MAP calibration.");
            result = result * calibration.Values[Text(transform, "gain")] - calibration.Values[Text(transform, "subtract")];
        }
        return Finite(result);
    }

    public double Calculate(string channelId, IReadOnlyDictionary<string, LoggerSample> inputs, DateTimeOffset now,
        TimeSpan maxAge, TimeSpan maxSkew)
    {
        if (maxAge <= TimeSpan.Zero || maxSkew < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        var channel = channels[channelId];
        if (Text(channel, "kind") != "calculated") throw new InvalidOperationException("This channel is queried directly.");
        var calculation = channel.GetProperty("calculation");
        var values = new List<LoggerSample>(2);
        foreach (var input in calculation.GetProperty("inputs").EnumerateArray())
        {
            if (!inputs.TryGetValue(input.GetString()!, out var sample) || !sample.IsValid || !double.IsFinite(sample.Value) ||
                sample.Timestamp > now || now - sample.Timestamp > maxAge)
                throw new TransportException("Calculated logger input is missing, invalid or stale.");
            values.Add(sample);
        }
        if ((values[0].Timestamp - values[1].Timestamp).Duration() > maxSkew)
            throw new TransportException("Calculated logger inputs are too far apart in time.");
        return Finite(Text(calculation, "operation") == "subtract" ? values[0].Value - values[1].Value :
            values[0].Value * values[1].Value / Number(calculation, "divisor"));
    }

    public double ConvertDisplay(string channelId, double canonicalValue, string unit, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var channel = channels[channelId];
        if (Text(channel, "unit") == unit) return Finite(canonicalValue);
        string canonicalUnit = Text(channel, "unit");
        if (canonicalUnit == "kPa" && unit == "psi") return Finite(canonicalValue / 6.894757293168);
        if (canonicalUnit == "kPa" && unit == "bar") return Finite(canonicalValue / 100);
        if (canonicalUnit == "degC" && unit == "degF") return Finite(canonicalValue * 1.8 + 32);
        if (canonicalUnit == "km/h" && unit == "mph") return Finite(canonicalValue / 1.609344);
        if (channel.TryGetProperty("displayConversions", out var conversions))
            foreach (var conversion in conversions.EnumerateArray())
                if (Text(conversion, "unit") == unit)
                {
                    if (Text(conversion, "operation") == "linear")
                        return Finite(canonicalValue * Number(conversion, "multiplier") + Number(conversion, "offset"));
                    if (Text(conversion, "operation") == "multiply_parameter" && parameters is not null &&
                        parameters.TryGetValue(Text(conversion, "parameter"), out double multiplier) && double.IsFinite(multiplier) && multiplier > 0)
                        return Finite(canonicalValue * multiplier);
                    throw new InvalidOperationException("This display requires an explicit valid conversion parameter.");
                }
        throw new InvalidOperationException("Unsupported logger display unit.");
    }

    private bool HasSoftwareMarker(ReadOnlySpan<byte> image)
    {
        // Gen2 has an address-bound complete identity, not a Gen1 SW filename marker.
        foreach (var profile in RomInspector.KnownProfiles.Where(p => p.Family == "Gen2" && SoftwareIds.Contains(p.Calibration)))
            if (image.Length >= profile.IdentityOffset + profile.Identity.Length &&
                image.Slice(profile.IdentityOffset, profile.Identity.Length).SequenceEqual(Encoding.ASCII.GetBytes(profile.Identity))) return true;
        foreach (string id in SoftwareIds)
        {
            if (RomInspector.KnownProfiles.Any(p => p.Family == "Gen2" && p.Calibration == id)) continue;
            if (image.IndexOf(Encoding.ASCII.GetBytes("SW-" + id + ".HEX")) >= 0) return true;
        }
        return false;
    }
    public LoggerCalibration BindRom(ReadOnlySpan<byte> image)
    {
        if (image.Length != 1_048_576) throw new InvalidDataException("Logger calibration requires a complete 1 MiB image.");
        if (!HasSoftwareMarker(image)) throw new InvalidDataException("Logger software ID does not match the ROM.");
        foreach (var fingerprint in root.GetProperty("codeFingerprints").EnumerateArray())
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var range in fingerprint.GetProperty("ranges").EnumerateArray())
            {
                int start = Hex(Text(range, "start")), length = range.GetProperty("length").GetInt32();
                if (length < 1 || start < 0 || start > image.Length - length) throw new InvalidDataException("Invalid logger code range.");
                hash.AppendData(image.Slice(start, length));
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(Text(fingerprint, "sha256"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ROM logger code differs from this definition.");
        }
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var binding in root.GetProperty("calibration").GetProperty("bindings").EnumerateObject())
        {
            string operation = Text(binding.Value, "operation");
            var sources = binding.Value.GetProperty("sources");
            if (sources.GetArrayLength() is < 1 or > 8 || (operation != "product" && (operation != "read" || sources.GetArrayLength() != 1)))
                throw new InvalidDataException("Unsupported logger calibration binding.");
            double result = 1;
            foreach (var source in sources.EnumerateArray())
            {
                int offset = Hex(Text(source, "offset"));
                if (Text(source, "storage") != "float32" || Text(source, "byteOrder") != "big" || offset < 0 || offset > image.Length - 4)
                    throw new InvalidDataException("Unsupported logger calibration storage.");
                double value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(image.Slice(offset, 4)));
                result *= Finite(value);
            }
            values.Add(binding.Name, Finite(result));
        }
        return new LoggerCalibration(values, Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant());
    }

    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing logger text.");
    private static int Hex(string value) => value.StartsWith("0x", StringComparison.Ordinal) ?
        int.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : throw new InvalidDataException("Expected hexadecimal logger address.");
    private static double Number(JsonElement node, string name) => Finite(node.GetProperty(name).GetDouble());
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite logger value.");
}
