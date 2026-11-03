using System.Globalization;
using System.Text.Json;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public class LiveLoggerTests
{
    private static LoggerDefinition Profile() => LoggerDefinition.Load(Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", "L38KEH000.logger.json"));
    private static LoggerCalibration Calibration() => LoggerCalibration.ExplicitMap(54.117647260427475, 1.6469999551773071);
    private static EcuIdentity Identity(string? calibration = "L38KEH000") => new("test", "offline fixture", null, calibration);
    private sealed class Peer : IReadTransport
    {
        public Dictionary<string, byte[]> Replies { get; } = [];
        public List<string> Requests { get; } = [];
        public Func<string, byte[]>? Override { get; set; }
        public string Mode => "test";
        public string Adapter => "offline fixture";
        public int ReadChunkSize => 3968;
        public bool ReadAttempted { get; set; }
        public Peer(LoggerDefinition profile)
        {
            using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", "L38KEH000.vectors.json")));
            foreach (var vector in data.RootElement.GetProperty("vectors").EnumerateArray())
                if (vector.GetProperty("type").GetString() == "decode")
                {
                    string id = vector.GetProperty("channel").GetString()!;
                    string request = Convert.ToHexString(profile.RequestFor(id));
                    Replies.TryAdd(request, Convert.FromHexString(vector.GetProperty("responseHex").GetString()!));
                }
        }
        public byte[] Exchange(byte[] request)
        {
            DiagnosticProtocol.CheckReadOnly(request, ReadChunkSize);
            string key = Convert.ToHexString(request); Requests.Add(key);
            return Override?.Invoke(key) ?? (request[0] == 1 ? [0x41, .. request.Skip(1).SelectMany(id => new[] { id }.Concat(Replies["2200" + id.ToString("X2")].Skip(3)))] : Replies[key]);
        }
        public void Dispose() { }
    }

    [Fact]
    public void All38ChannelsUseVerifiedReadOnlyBatchesWithoutEnteringRomReadSession()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), definition.ChannelIds);
        var frame = logger.ReadFrame();
        Assert.Equal(34, logger.UngroupedQueryCount); Assert.Equal(30, logger.QueryCount); Assert.Equal(30, peer.Requests.Count);
        Assert.Equal(30, peer.Requests.Distinct().Count()); Assert.All(peer.Requests, request => Assert.True(request.StartsWith("22", StringComparison.Ordinal) || request.StartsWith("01", StringComparison.Ordinal)));
        Assert.False(peer.ReadAttempted); Assert.Equal(38, frame.Readings.Count);
        Assert.All(frame.Readings.Values, value => { Assert.NotNull(value.Value); Assert.Equal("OK", value.Status); Assert.NotNull(value.Timestamp); });
        Assert.Equal(30, frame.Exchanges.Count); Assert.Equal(1, frame.Scan);
        Assert.Equal(2, logger.ReadFrame().Scan);
    }

    [Fact]
    public void SubsetAutomaticallyIncludesDependenciesAndDeduplicatesSharedMapQuery()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["boost_pressure", "map_sensor_voltage", "injector_duty_cycle"]);
        var frame = logger.ReadFrame();
        Assert.Equal(4, logger.QueryCount); Assert.Equal(3, frame.Readings.Count);
        Assert.Equal(1, peer.Requests.Count(request => request == "220900"));
        Assert.DoesNotContain("barometric_pressure", frame.Readings.Keys);
        Assert.All(frame.Readings.Values, value => Assert.NotNull(value.Value));
        Assert.Equal("220900", peer.Requests[0]); Assert.Equal("220033", peer.Requests[1]);
    }

    [Fact]
    public void IdentityAndSessionGuardsRejectBeforeIo()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        foreach (string? id in new string?[] { null, "L3M7EF000", "XL38KEH000", "L38KEH0001" })
            Assert.Throws<TransportException>(() => new LiveLogger(definition, Calibration(), peer, Identity(id), ["engine_rpm"]));
        Assert.True(definition.MatchesCalibrationId("SW-L38KEH000.HEX"));
        peer.ReadAttempted = true;
        Assert.Throws<TransportException>(() => new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm"]));
        Assert.Empty(peer.Requests);
    }

    [Fact]
    public void EmptyAndUnknownSelectionsFailBeforeIo()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        Assert.Throws<ArgumentException>(() => new LiveLogger(definition, Calibration(), peer, Identity(), []));
        Assert.Throws<ArgumentException>(() => new LiveLogger(definition, Calibration(), peer, Identity(), ["unknown"]));
        Assert.Empty(peer.Requests);
    }

    [Fact]
    public void UnsupportedSharedQueryRemainsUnavailableAndIsNotRetriedOnLaterScans()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        peer.Override = key => key == "22A211" ? throw new NegativeResponseException(0x22, 0x31) : peer.Replies[key];
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["clutch_switch", "neutral_switch", "engine_rpm"]);
        foreach (var frame in new[] { logger.ReadFrame(), logger.ReadFrame() })
        {
            Assert.Null(frame.Readings["clutch_switch"].Value); Assert.Null(frame.Readings["neutral_switch"].Value);
            Assert.Contains("31", frame.Readings["neutral_switch"].Status); Assert.NotNull(frame.Readings["engine_rpm"].Value);
        }
        Assert.Equal(1, peer.Requests.Count(key => key == "22A211")); Assert.Equal(1, logger.ActiveQueryCount);
    }

    [Fact]
    public void TransientConditionsRejectionCanBeReadOnNextScheduledScan()
    {
        var definition = Profile(); using var peer = new Peer(definition); int count = 0;
        peer.Override = key => ++count == 1 ? throw new NegativeResponseException(0x22, 0x22) : peer.Replies[key];
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm"]);
        Assert.Null(logger.ReadFrame().Readings["engine_rpm"].Value);
        Assert.NotNull(logger.ReadFrame().Readings["engine_rpm"].Value); Assert.Equal(2, peer.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedOrDisconnectedResponseStopsWithoutRetryAndPreservesPartialEvidence(bool malformed)
    {
        var definition = Profile(); using var peer = new Peer(definition);
        peer.Override = _ => malformed ? [0x62, 0, 0, 0] : throw new TransportException("USB removed");
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        Assert.Throws<TransportException>(() => logger.ReadFrame()); Assert.Single(peer.Requests);
        Assert.NotNull(logger.LastFrame); Assert.Single(logger.LastFrame.Exchanges);
        Assert.NotNull(logger.LastFrame.Exchanges[0].Error); Assert.Null(logger.LastFrame.Readings["engine_rpm"].Value);
    }

    [Fact]
    public void CancellationStopsBeforeTheNextExchange()
    {
        var definition = Profile(); using var peer = new Peer(definition); using var stop = new CancellationTokenSource();
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        Assert.Throws<OperationCanceledException>(() => logger.ReadFrame(stop.Token, _ => stop.Cancel()));
        Assert.Single(peer.Requests); Assert.NotNull(logger.LastFrame); Assert.Single(logger.LastFrame.Exchanges);
        using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => logger.ReadFrame(preCancelled.Token)); Assert.Single(peer.Requests);
    }

    [Fact]
    public void ExcessiveInputSkewDoesNotProduceInjectorDuty()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        peer.Override = key => { if (key == "22000C") Thread.Sleep(550); return peer.Replies[key]; };
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["injector_duty_cycle"]);
        var reading = logger.ReadFrame().Readings["injector_duty_cycle"];
        Assert.Null(reading.Value); Assert.Contains("apart", reading.Status);
    }
    [Fact]
    public void CancellationRetainsPartialFrameEvenWithoutAProgressCallback()
    {
        var definition = Profile(); using var peer = new Peer(definition); using var stop = new CancellationTokenSource();
        peer.Override = key => { stop.Cancel(); return peer.Replies[key]; };
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        Assert.Throws<OperationCanceledException>(() => logger.ReadFrame(stop.Token));
        Assert.Single(peer.Requests); Assert.NotNull(logger.LastFrame); Assert.Single(logger.LastFrame.Exchanges);
        Assert.NotNull(logger.LastFrame.Readings["engine_rpm"].Value); Assert.Null(logger.LastFrame.Readings["knock_retard"].Value);
    }

    [Fact]
    public void CleanStopFinishesTheCurrentExchangeAndAllowsANewLoggerOnTheSameTransport()
    {
        var definition = Profile(); using var peer = new Peer(definition); using var stop = new CancellationTokenSource();
        peer.Override = key => { stop.Cancel(); return peer.Replies[key]; };
        var first = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        Assert.Throws<OperationCanceledException>(() => first.ReadFrame(stop.Token));
        Assert.Single(peer.Requests);
        Assert.NotNull(first.LastFrame!.Readings["engine_rpm"].Value);
        peer.Override = null;
        var next = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        var frame = next.ReadFrame();
        Assert.Equal(1, frame.Scan);
        Assert.Equal(3, peer.Requests.Count);
        Assert.All(frame.Readings.Values, reading => Assert.NotNull(reading.Value));
    }

    [Fact]
    public void DisplayUnitsPreserveVacuumAndRequireExplicitAfrStoichiometry()
    {
        var definition = Profile();
        Assert.Equal(-10, definition.ConvertDisplay("boost_pressure", -68.94757293168, "psi"), 10);
        Assert.Equal(1, definition.ConvertDisplay("fuel_rail_pressure", 100, "bar"));
        Assert.Equal(32, definition.ConvertDisplay("coolant_temperature", 0, "degF"));
        Assert.Equal(60, definition.ConvertDisplay("vehicle_speed", 96.56064, "mph"), 10);
        Assert.Throws<InvalidOperationException>(() => definition.ConvertDisplay("actual_lambda", 1, "AFR"));
        Assert.Equal(9.8, definition.ConvertDisplay("actual_lambda", 1, "AFR", new Dictionary<string, double> { ["stoichiometric_afr"] = 9.8 }));
        Assert.Contains("psi", definition.Channels.Single(c => c.Id == "boost_pressure").DisplayUnits);
    }

    [Fact]
    public void OnlyExactReadIdentifierShapeIsAddedToTransportAllowlist()
    {
        DiagnosticProtocol.CheckReadOnly([0x22, 0x17, 0x46]);
        foreach (byte[] request in new byte[][] { [0x22], [0x22, 0x17], [0x22, 0x17, 0x46, 0], [0x22, 0xF1, 0xA0], [0x2E, 0x17, 0x46], [0x31, 1, 0], [0x34, 0, 0], [0x36, 1] })
            Assert.Throws<TransportException>(() => DiagnosticProtocol.CheckReadOnly(request));
    }

    [Fact]
    public void Fast_partial_and_complete_frames_keep_one_recording_clock_origin()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
        using var temporary = new TempFolder();
        using var file = new LoggerRecording(temporary.Path, definition, Calibration(), Identity(), ["engine_rpm", "knock_retard"],
            new Dictionary<string, string> { ["engine_rpm"] = "rpm", ["knock_retard"] = "deg" }, null, "test", 0);
        long? originTicks = null;
        void Check(LoggerFrame frame)
        {
            long ticks = frame.Timestamp.UtcTicks - frame.Elapsed.Ticks;
            originTicks ??= ticks;
            Assert.Equal(originTicks.Value, ticks);
        }
        for (int i = 0; i < 200; i++)
        {
            var frame = logger.ReadFrame(progress: Check);
            Check(frame); file.Append(frame);
            if (i % 25 == 0) Thread.Yield();
        }
        file.Complete("Stopped");
        Assert.Equal(200, RecordedLog.Load(Path.Combine(file.Folder, "log.csv")).Rows);
    }

    [Fact]
    public void RecordingKeepsUnitsPerChannelTimeUnavailableBlanksAndRawReplies()
    {
        var definition = Profile(); using var peer = new Peer(definition);
        using var temporary = new TempFolder();
        var parent = temporary.Path;
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            peer.Override = key => key == "221746" ? throw new NegativeResponseException(0x22, 0x31) : peer.Replies[key];
            var logger = new LiveLogger(definition, Calibration(), peer, Identity(), ["engine_rpm", "knock_retard"]);
            string folder;
            using (var file = new LoggerRecording(parent, definition, Calibration(), Identity(), ["engine_rpm", "knock_retard"],
                new Dictionary<string, string> { ["engine_rpm"] = "rpm", ["knock_retard"] = "deg" }, null, "test", 5))
            {
                folder = file.Folder; file.Append(logger.ReadFrame()); file.Complete("Stopped");
                Assert.Throws<ObjectDisposedException>(() => file.Append(logger.ReadFrame()));
            }
            string[] rows = File.ReadAllLines(Path.Combine(folder, "log.csv")); Assert.Equal(2, rows.Length);
            Assert.Contains("Engine RPM (rpm)", rows[0]); Assert.Contains("engine_rpm sample UTC", rows[0]);
            Assert.Contains("\"\",\"\",\"Unavailable · NRC 31\"", rows[1]);
            string[] wire = File.ReadAllLines(Path.Combine(folder, "responses.jsonl")); Assert.Equal(2, wire.Length);
            using var nrc = JsonDocument.Parse(wire[1]); Assert.Equal("7F2231", nrc.RootElement.GetProperty("Error").GetString());
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "session.json")));
            Assert.Equal("Stopped", manifest.RootElement.GetProperty("status").GetString());
            Assert.False(manifest.RootElement.GetProperty("atomicEcuSnapshot").GetBoolean());
            Assert.Equal(1, manifest.RootElement.GetProperty("rows").GetInt32());
            using var another = new LoggerRecording(parent, definition, Calibration(), Identity(), ["engine_rpm"],
                new Dictionary<string, string> { ["engine_rpm"] = "rpm" }, null, "test", 5);
            Assert.NotEqual(folder, another.Folder);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
