using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public class MultiRomLoggerTests
{
    public static TheoryData<string, int, int, int> Profiles => new()
    {
        { "L38KEH000", 38, 34, 298 }, { "L39SED000", 38, 34, 298 },
        { "L39SEE000", 38, 34, 298 }, { "L39SEF000", 38, 34, 298 },
        { "L3M7EF000", 35, 31, 277 }, { "L35HEH000", 36, 32, 284 },
        { "L33XEH000", 35, 31, 277 }, { "L3P4EM000", 35, 31, 277 }, { "L3CGEE000", 20, 19, 77 },
        { "L3M6EG000", 33, 30, 247 }
    };
    private static string Data(string id, string kind) => Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", $"{id}.{kind}.json");
    private static JsonDocument Vectors(string id) => JsonDocument.Parse(File.ReadAllBytes(Data(id, "vectors")));
    private static LoggerCalibration Calibration(JsonElement root) => LoggerCalibration.ExplicitMap(
        root.GetProperty("calibration").GetProperty("map_gain_kpa_per_v").GetDouble(),
        root.GetProperty("calibration").GetProperty("map_offset_kpa").GetDouble());

    [Theory, MemberData(nameof(Profiles))]
    public void CompleteEcuIdentitySelectsOnlyItsKnownLogger(string id, int channels, int queries, int vectors)
    {
        var definition = LoggerDefinition.ForCalibration(id)!;
        string complete = RomInspector.KnownProfiles.Single(p => p.Calibration == id).Identity;
        foreach (string reported in new[] { id, complete, "SW-" + id + ".HEX", "SW-" + complete + ".HEX" })
        {
            Assert.True(definition.MatchesCalibrationId(reported));
            Assert.Same(definition, LoggerDefinition.ForCalibration(reported));
        }
        foreach (string invalid in new[] { id + "1", id + "L3T9999", complete[..^1], complete + "1", "X" + complete,
            complete[..^1] + (complete[^1] == '0' ? "1" : "0") })
        {
            Assert.False(definition.MatchesCalibrationId(invalid));
            Assert.Null(LoggerDefinition.ForCalibration(invalid));
        }
        string other = RomInspector.KnownProfiles.First(p => p.Calibration != id).Identity;
        Assert.False(definition.MatchesCalibrationId(other));
        Assert.Null(LoggerDefinition.ForCalibration(complete + " " + other));
        using var data = Vectors(id); using var peer = new Peer(definition, data.RootElement);
        var logger = new LiveLogger(definition, Calibration(data.RootElement), peer,
            new EcuIdentity("test", "fixture", null, complete), definition.ChannelIds);
        var frame = logger.ReadFrame();
        Assert.Equal(channels, frame.Readings.Count); Assert.Equal(queries, peer.Requests.Count);
        Assert.Equal(vectors, data.RootElement.GetProperty("vectors").GetArrayLength());
        Assert.All(frame.Readings.Values, reading => Assert.Equal("OK", reading.Status));
    }

    [Fact]
    public void CatalogCoversExactlyEveryBundledReaderDefinition()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", "Catalog.json")));
        var expected = Profiles.Select(row => (string)row[0]).Order().ToArray();
        Assert.Equal(expected, LoggerDefinition.Known.SelectMany(p => p.SoftwareIds).Order().ToArray());
        Assert.Equal(expected, RomInspector.KnownProfiles.Select(p => p.Calibration).Order().ToArray());
        Assert.Equal(2631, Profiles.Sum(row => (int)row[3]));
        Assert.Equal(346, LoggerDefinition.Known.Sum(p => p.ChannelIds.Count));
        Assert.Equal(38, LoggerDefinition.Known.Sum(p => p.UnavailableChannels.Count));
        foreach (var profile in document.RootElement.GetProperty("profiles").EnumerateArray())
        {
            string path = Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", profile.GetProperty("profileFile").GetString()!);
            Assert.Equal(profile.GetProperty("definitionSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }

    [Theory, MemberData(nameof(Profiles))]
    public void NativeVectorsDecodeAndEveryChannelRetainsPendingEcuValidation(string id, int channels, int queries, int vectors)
    {
        var profile = LoggerDefinition.ForCalibration(id)!;
        Assert.NotNull(profile); Assert.Equal(channels, profile.ChannelIds.Count); Assert.Equal(queries, profile.QueryIds.Count);
        using var data = Vectors(id); var calibration = Calibration(data.RootElement);
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1); var covered = new HashSet<string>(); int count = 0;
        foreach (var vector in data.RootElement.GetProperty("vectors").EnumerateArray())
        {
            string channel = vector.GetProperty("channel").GetString()!;
            double actual = vector.GetProperty("type").GetString() == "decode"
                ? profile.Decode(channel, Convert.FromHexString(vector.GetProperty("responseHex").GetString()!), calibration)
                : profile.Calculate(channel, vector.GetProperty("inputs").EnumerateObject().ToDictionary(p => p.Name,
                    p => new LoggerSample(p.Value.GetDouble(), now)), now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
            double expected = vector.GetProperty("expected").GetDouble();
            Assert.InRange(Math.Abs(actual - expected), 0, Math.Max(1e-10, Math.Abs(expected) * 2e-12));
            covered.Add(channel); count++;
        }
        Assert.Equal(vectors, count); Assert.True(covered.SetEquals(profile.ChannelIds));
        using var definition = JsonDocument.Parse(File.ReadAllBytes(Data(id, "logger")));
        foreach (var channel in definition.RootElement.GetProperty("channels").EnumerateArray())
            Assert.Equal("pending", channel.GetProperty("verification").GetProperty("ecu").GetString());
        Assert.False(definition.RootElement.GetProperty("validation").GetProperty("realEcuTested").GetBoolean());
        Assert.False(definition.RootElement.GetProperty("validation").GetProperty("loggerAdapterBenchTested").GetBoolean());
    }

    private sealed class Peer(LoggerDefinition profile, JsonElement vectors) : IReadTransport
    {
        private readonly Dictionary<string, byte[]> replies = vectors.GetProperty("vectors").EnumerateArray()
            .Where(v => v.GetProperty("type").GetString() == "decode")
            .GroupBy(v => Convert.ToHexString(profile.RequestFor(v.GetProperty("channel").GetString()!)))
            .ToDictionary(g => g.Key, g => Convert.FromHexString(g.First().GetProperty("responseHex").GetString()!));
        public List<string> Requests { get; } = [];
        public string Mode => "test";
        public string Adapter => "offline fixture";
        public int ReadChunkSize => 3968;
        public bool ReadAttempted { get; set; }
        public byte[] Exchange(byte[] request)
        {
            DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
            string key = Convert.ToHexString(request); Requests.Add(key); return replies[key];
        }
        public void Dispose() { }
    }

    [Theory, MemberData(nameof(Profiles))]
    public void PollingUsesOnlyThisProfilesAvailableQueriesAndRejectsWrongIdentityBeforeIo(string id, int channels, int queries, int vectors)
    {
        var profile = LoggerDefinition.ForCalibration(id)!; using var data = Vectors(id);
        Assert.Equal(vectors, data.RootElement.GetProperty("vectors").GetArrayLength());
        using var peer = new Peer(profile, data.RootElement);
        foreach (string? wrong in new[] { null, "UNKNOWN", id + "1", "L38KEH000 L39SED000", id == "L38KEH000" ? "L39SED000" : "L38KEH000" })
            Assert.Throws<TransportException>(() => new LiveLogger(profile, Calibration(data.RootElement), peer,
                new EcuIdentity("test", "fixture", null, wrong), profile.ChannelIds));
        Assert.Empty(peer.Requests);
        var logger = new LiveLogger(profile, Calibration(data.RootElement), peer,
            new EcuIdentity("test", "fixture", null, "SW-" + id + ".HEX"), profile.ChannelIds);
        var frame = logger.ReadFrame();
        Assert.Equal(channels, frame.Readings.Count); Assert.Equal(queries, peer.Requests.Count);
        Assert.Equal(profile.QueryIds.Order(), peer.Requests.Select(r => r[2..]).Order());
        Assert.All(frame.Readings.Values, r => { Assert.NotNull(r.Value); Assert.Equal("OK", r.Status); });
        foreach (var missing in profile.UnavailableChannels) Assert.DoesNotContain(missing.Id, frame.Readings.Keys);
        Assert.False(peer.ReadAttempted);
    }

    [Theory, MemberData(nameof(Profiles))]
    public void BindingUsesOwnCalibrationSlotsAndProtectsEveryCodeGuard(string id, int channels, int queries, int vectors)
    {
        var node = JsonNode.Parse(File.ReadAllText(Data(id, "logger")))!;
        byte[] image = Enumerable.Repeat((byte)0xA5, 1_048_576).ToArray();
        Encoding.ASCII.GetBytes("SW-" + id + ".HEX").CopyTo(image, 0x100);
        var romProfile = RomInspector.KnownProfiles.Single(p => p.Calibration == id);
        if (romProfile.Family == "Gen2") Encoding.ASCII.GetBytes(romProfile.Identity).CopyTo(image, romProfile.IdentityOffset);
        var bindings = node["calibration"]!["bindings"]!;
        int Offset(JsonNode source) => Convert.ToInt32(source["offset"]!.GetValue<string>()[2..], 16);
        var gainSources = bindings["map_gain_kpa_per_v"]!["sources"]!.AsArray();
        int first = Offset(gainSources[0]!), second = Offset(gainSources[1]!);
        int subtract = Offset(bindings["map_offset_kpa"]!["sources"]![0]!);
        void Write(int address, float value) => BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(address, 4), BitConverter.SingleToInt32Bits(value));
        Write(first, 250); Write(second, .25f); Write(subtract, 3);
        foreach (var fingerprint in node["codeFingerprints"]!.AsArray())
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var range in fingerprint!["ranges"]!.AsArray())
            {
                int start = Convert.ToInt32(range!["start"]!.GetValue<string>()[2..], 16), length = range["length"]!.GetValue<int>();
                Assert.DoesNotContain(new[] { first, second, subtract }, slot => slot < start + length && slot + 4 > start);
                hash.AppendData(image.AsSpan(start, length));
            }
            fingerprint["sha256"] = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        var profile = LoggerDefinition.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Equal(channels, profile.ChannelIds.Count); Assert.Equal(queries, profile.QueryIds.Count);
        using var vectorData = Vectors(id); Assert.Equal(vectors, vectorData.RootElement.GetProperty("vectors").GetArrayLength());
        Assert.Equal(62.5, profile.BindRom(image).Values["map_gain_kpa_per_v"]);
        Assert.Equal(3, profile.BindRom(image).Values["map_offset_kpa"]);
        Write(first, 300); Assert.Equal(75, profile.BindRom(image).Values["map_gain_kpa_per_v"]);
        foreach (var fingerprint in node["codeFingerprints"]!.AsArray())
        {
            int start = Convert.ToInt32(fingerprint!["ranges"]![0]!["start"]!.GetValue<string>()[2..], 16);
            image[start] ^= 1; Assert.Throws<InvalidDataException>(() => profile.BindRom(image)); image[start] ^= 1;
        }
        image[romProfile.Family == "Gen2" ? romProfile.IdentityOffset : 0x103] = (byte)'X'; Assert.Throws<InvalidDataException>(() => profile.BindRom(image));
    }

    [Fact]
    public void UnknownAndAmbiguousImagesCannotSelectAnotherProfile()
    {
        Assert.Null(LoggerDefinition.ForCalibration(null)); Assert.Null(LoggerDefinition.ForCalibration("UNKNOWN"));
        Assert.Null(LoggerDefinition.ForCalibration("L38KEH000 L39SED000"));
        byte[] image = new byte[1_048_576];
        Assert.Throws<InvalidDataException>(() => LoggerDefinition.BindKnownRom(image));
        Encoding.ASCII.GetBytes("SW-L38KEH000.HEX SW-L39SED000.HEX").CopyTo(image, 0x100);
        Assert.Throws<InvalidDataException>(() => LoggerDefinition.BindKnownRom(image));
    }
}
