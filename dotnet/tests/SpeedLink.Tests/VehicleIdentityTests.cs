using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public class VehicleIdentityTests
{
    private const string Chassis = "GG3P123456"; // Synthetic, not a customer identifier.
    private static byte[] Reply(string value) => [0x49, 2, 1, .. Encoding.ASCII.GetBytes(value)];
    private static TestEcu Peer(string value) => new(Fixture.Rom)
    {
        ModifyResponse = (request, response) => request.AsSpan().SequenceEqual(new byte[] { 9, 2 }) ? Reply(value) : response
    };

    [Theory]
    [InlineData("GG3P123456       ")]
    [InlineData("GG3P123456\0\0\0\0\0\0\0")]
    [InlineData("GG3P123456\0 \0 \0  ")]
    [InlineData("GG3P123456")]
    [InlineData("GG3P-123456      ")]
    [InlineData("BL3FW123456      ")]
    public void chassis_identification_retains_calibration_without_inventing_a_vin(string value)
    {
        using var peer = Peer(value);
        var identity = ReadWorkflow.Identify(peer);
        string expected = value.TrimEnd('\0', ' ');
        Assert.Null(identity.Vin); Assert.Equal(expected, identity.ChassisId);
        Assert.Equal(expected, identity.VehicleIdentifier); Assert.Equal("Chassis ID", identity.VehicleIdentifierLabel);
        Assert.NotNull(identity.VinStatus); Assert.NotNull(identity.Calibration);
        Assert.Equal(new[] { "0902", "0904" }, peer.Requests.Select(Convert.ToHexString));
        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(identity.ToManifest()));
        Assert.Equal(expected, metadata.RootElement.GetProperty("chassisId").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("vin").ValueKind);
    }

    [Theory]
    [InlineData("JM1GG12L061234567")]
    [InlineData("JM1GG12L061234567\0")]
    [InlineData("JM1GG12L061234567  ")]
    public void standard_vins_keep_their_existing_identity_and_label(string value)
    {
        using var peer = Peer(value); var identity = ReadWorkflow.Identify(peer);
        Assert.Equal("JM1GG12L061234567", identity.Vin); Assert.Null(identity.ChassisId);
        Assert.Equal("VIN", identity.VehicleIdentifierLabel); Assert.Null(identity.VinStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("                 ")]
    [InlineData("gg3p123456       ")]
    [InlineData("GG3P12 456       ")]
    [InlineData("GG3P12345        ")]
    [InlineData(" GG3P123456      ")]
    [InlineData("GG3P123456\0X")]
    [InlineData("GG3P123456\n")]
    [InlineData("JM1GG12L06123456I")]
    [InlineData("JM1GG12L061234567X")]
    [InlineData("UNKNOWN")]
    public void malformed_or_arbitrary_vehicle_identifiers_are_still_rejected(string value)
    {
        using var peer = Peer(value);
        Assert.Throws<TransportException>(() => ReadWorkflow.Identify(peer));
        Assert.Single(peer.Requests);
    }

    [Fact]
    public void malformed_identification_headers_are_still_rejected()
    {
        using var peer = Peer(Chassis);
        peer.ModifyResponse = (request, response) => request[0] == 9 && request[1] == 2 ? [0x49, 2, 2, .. Encoding.ASCII.GetBytes(Chassis)] : response;
        Assert.Throws<TransportException>(() => ReadWorkflow.Identify(peer));
        Assert.Single(peer.Requests);
    }

    [Fact]
    public void elm_multiframe_and_j2534_decode_the_same_padded_chassis_response()
    {
        byte[] response = Reply(Chassis.PadRight(17));
        byte[] parsed = Assert.Single(DiagnosticProtocol.ParseElm(Fixture.Multiline(response), "0902"));
        Assert.Equal(response, parsed);
        var channel = new IdentityChannel();
        channel.Replies.Enqueue(new(0x7E8, 0, parsed));
        channel.Replies.Enqueue(new(0x7E8, 0, [0x49, 4, 1, .. "UNKNOWN01"u8.ToArray()]));
        using var link = new J2534ReadTransport(channel); link.Initialize();
        var identity = ReadWorkflow.Identify(link);
        Assert.Equal(Chassis, identity.ChassisId); Assert.Equal("UNKNOWN01", identity.Calibration);
        Assert.Equal(2, channel.Writes.Count);
    }

    [Theory]
    [InlineData("chassis")]
    [InlineData("calibration")]
    [InlineData("kind")]
    public void changed_vehicle_identity_blocks_backup_before_read_session(string change)
    {
        using var temp = new TempFolder(); using var peer = Peer(Chassis.PadRight(17));
        var expected = ReadWorkflow.Identify(peer);
        expected = change switch
        {
            "chassis" => expected with { ChassisId = "GG3P654321" },
            "calibration" => expected with { Calibration = "OTHER0001" },
            _ => expected with { Vin = "JM1GG12L061234567", ChassisId = null }
        };
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(peer, expected, temp.Path));
        Assert.False(peer.ReadSession); Assert.DoesNotContain(peer.Requests, r => r[0] is 0x10 or 0x27 or 0x23);
        Assert.Empty(Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public void unknown_calibration_with_chassis_id_can_create_a_two_pass_verified_backup()
    {
        using var temp = new TempFolder(); using var peer = Peer(Chassis.PadRight(17));
        peer.ModifyResponse = (request, response) => Convert.ToHexString(request) switch
        {
            "0902" => Reply(Chassis.PadRight(17)),
            "0904" => [0x49, 4, 1, .. "UNKNOWN01"u8.ToArray()],
            _ => response
        };
        var result = Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path);
        var backup = Fixture.Store().Load(result.Path, "simulator");
        Assert.Equal(Fixture.Rom, backup.Data);
        Assert.Equal(Chassis, backup.Manifest["identity"]!["chassisId"]!.GetValue<string>());
        Assert.Null(LoggerDefinition.ForCalibration("UNKNOWN01"));
    }

    [Fact]
    public void changed_padding_is_not_a_changed_vehicle()
    {
        using var temp = new TempFolder(); using var peer = Peer(Chassis.PadRight(17));
        var identity = ReadWorkflow.Identify(peer);
        peer.ModifyResponse = (request, response) => request.AsSpan().SequenceEqual(new byte[] { 9, 2 }) ? Reply(Chassis.PadRight(17, '\0')) : response;
        var backup = Fixture.Store().Load(Fixture.Reader().ReadBackup(peer, identity, temp.Path).Path);
        Assert.Equal(Fixture.Rom, backup.Data);
    }

    [Fact]
    public void chassis_id_does_not_bypass_logger_calibration_binding()
    {
        var profile = LoggerDefinition.Speed6;
        var calibration = LoggerCalibration.ExplicitMap(54, 1);
        using var peer = Peer(Chassis);
        var identity = new EcuIdentity("simulator", "offline fixture", null, profile.SoftwareIds.Single()) { ChassisId = Chassis };
        Assert.NotNull(new LiveLogger(profile, calibration, peer, identity, ["engine_rpm"]));
        Assert.Throws<TransportException>(() => new LiveLogger(profile, calibration, peer, identity with { Calibration = "UNKNOWN01" }, ["engine_rpm"]));
        Assert.Empty(peer.Requests);
    }

    [Fact]
    public async Task chassis_text_and_hex_are_redacted_from_support_exports()
    {
        using var temp = new TempFolder();
        await using var session = new DiagnosticSession(temp.Path, "test", "test");
        using var peer = Peer(Chassis.PadRight(17)); using var link = new DiagnosticTransport(peer, session);
        var identity = ReadWorkflow.Identify(link);
        session.Record("identity", new { identity.ChassisId, message = "Chassis ID: " + Chassis, raw = Convert.ToHexString(Encoding.ASCII.GetBytes(Chassis)) });
        await session.FlushAsync(); string path = Path.Combine(temp.Path, "support.zip"); SupportReport.Export(session.Folder, path);
        using var zip = ZipFile.OpenRead(path);
        string text = string.Join('\n', zip.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
        Assert.DoesNotContain(Chassis, text); Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(Chassis)), text);
        Assert.Contains("[redacted]", text);
        Assert.DoesNotContain(Chassis, new DiagnosticRedactor().Text("Chassis ID: " + Chassis));
    }

    private sealed class IdentityChannel : IJ2534Channel
    {
        public Queue<J2534Frame> Replies { get; } = new();
        public List<byte[]> Writes { get; } = [];
        public string? CleanupWarning => null;
        public J2534Version Open() => new("test", "test", "04.04");
        public void Connect(uint bitrate, uint requestId, uint responseId) { }
        public void ClearReceive() { }
        public void Write(byte[] request) => Writes.Add(request.ToArray());
        public J2534Frame? Read(int timeoutMilliseconds) => Replies.TryDequeue(out var reply) ? reply : null;
        public void Dispose() { }
    }
}
