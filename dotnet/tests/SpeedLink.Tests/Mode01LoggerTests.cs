using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class Mode01LoggerTests
{
    [Fact]
    public void All_enabled_reference_aliases_match_independent_original_handler_fixtures()
    {
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", "Mode01_Aliases.vectors.json")));
        int profiles = 0, cases = 0;
        foreach (var profile in data.RootElement.GetProperty("profiles").EnumerateArray())
        {
            profiles++;
            foreach (var fixture in profile.GetProperty("cases").EnumerateArray())
            {
                cases++;
                var request = Convert.FromHexString(fixture.GetProperty("requestHex").GetString()!);
                var reply = Convert.FromHexString(fixture.GetProperty("responseHex").GetString()!);
                var split = Mode01LoggerProtocol.Split(request, reply);
                foreach (byte id in request.Skip(1)) Assert.Equal(Enumerable.Range(0, split[id].Length).Select(n => (byte)(id + n * 43)), split[id]);
            }
        }
        Assert.Equal(9, profiles); Assert.Equal(45, cases);
    }

    private static LiveLogger Logger(Peer peer) => new(LoggerDefinition.Speed6, LoggerCalibration.ExplicitMap(100, 0), peer,
        new EcuIdentity("test", "fixture", null, "L38KEH000"), ["engine_rpm", "fuel_rail_pressure"]);

    [Fact]
    public void Verified_aliases_share_one_reply_without_changing_units_or_precision()
    {
        using var peer = new Peer(); var logger = Logger(peer); var frame = logger.ReadFrame();
        Assert.Equal(2, logger.UngroupedQueryCount); Assert.Equal(1, logger.QueryCount);
        Assert.Equal("010C23", Assert.Single(peer.Requests));
        Assert.Equal(2000, frame.Readings["engine_rpm"].Value); Assert.Equal(1000, frame.Readings["fuel_rail_pressure"].Value);
        Assert.Equal(frame.Readings["engine_rpm"].Timestamp, frame.Readings["fuel_rail_pressure"].Timestamp);
        Assert.Equal("010C23", Assert.Single(frame.Exchanges).RequestHex);
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x31)]
    public void Completed_batch_rejection_uses_individual_reads_only_on_the_next_scan(int code)
    {
        using var peer = new Peer { Rejection = code }; var logger = Logger(peer);
        var first = logger.ReadFrame();
        Assert.Single(peer.Requests); Assert.All(first.Readings.Values, r => Assert.Null(r.Value));
        Assert.Equal(2, logger.QueryCount); Assert.Equal(2, logger.ActiveQueryCount);
        var next = logger.ReadFrame();
        Assert.Equal(new[] { "010C23", "22000C", "220023" }, peer.Requests);
        Assert.All(next.Readings.Values, r => Assert.Equal("OK", r.Status));
        logger.ReadFrame(); Assert.Equal(1, peer.Requests.Count(r => r.StartsWith("01", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("410C1F40240064")]
    [InlineData("410C1F402300")]
    [InlineData("412300640C1F40")]
    [InlineData("410C1F4023006400")]
    public void Malformed_batch_is_not_retried_or_silently_replaced(string response)
    {
        using var peer = new Peer { Batch = Convert.FromHexString(response) }; var logger = Logger(peer);
        Assert.Throws<TransportException>(() => logger.ReadFrame()); Assert.Single(peer.Requests);
        Assert.NotNull(logger.LastFrame); Assert.All(logger.LastFrame.Readings.Values, r => Assert.Null(r.Value));
    }

    [Theory]
    [InlineData("010C0C")]
    [InlineData("0104")]
    [InlineData("010C230C")]
    [InlineData("040C")]
    [InlineData("010C430E102300")]
    public void Unknown_duplicate_or_non_read_identifiers_are_blocked(string hex)
    {
        byte[] request = Convert.FromHexString(hex);
        Assert.False(Mode01LoggerProtocol.IsAllowed(request));
        Assert.Throws<TransportException>(() => DiagnosticProtocol.CheckReadOnly(request));
    }

    private sealed class Peer : IReadTransport
    {
        public List<string> Requests { get; } = [];
        public int? Rejection { get; init; }
        public byte[] Batch { get; init; } = [0x41, 0x0C, 0x1F, 0x40, 0x23, 0, 0x64];
        public string Mode => "test";
        public string Adapter => "fixture";
        public int ReadChunkSize => 3968;
        public bool ReadAttempted { get; set; }
        public byte[] Exchange(byte[] request)
        {
            DiagnosticProtocol.CheckReadOnly(request); string hex = Convert.ToHexString(request); Requests.Add(hex);
            if (request[0] == 1)
            {
                if (Rejection is int code) throw new NegativeResponseException(1, (byte)code);
                return Batch;
            }
            return hex switch { "22000C" => [0x62, 0, 0x0C, 0x1F, 0x40], "220023" => [0x62, 0, 0x23, 0, 0x64], _ => throw new InvalidOperationException() };
        }
        public void Dispose() { }
    }
}
