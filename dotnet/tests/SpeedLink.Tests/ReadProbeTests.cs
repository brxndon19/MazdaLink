using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class ReadProbeTests
{
    [Fact]
    public void Gen1ProbeUnlocksOnceAndReadsOnlyTwoSamples()
    {
        using var ecu = new TestEcu(Fixture.Rom) { Seed = [0x12, 0x34, 0x56] };
        var result = ReadProbe.Run(ecu);
        Assert.True(result.Passed);
        Assert.Equal(ReadFamily.Gen1, result.Family);
        Assert.True(result.SecurityKeyRequired);
        Assert.Null(result.UpperReadError);
        Assert.Equal(Fixture.Profiles[0].Calibration, result.Identity.Calibration);
        var reads = ecu.Requests.Where(r => r[0] == 0x23).Select(r => Convert.ToHexString(r)).ToArray();
        Assert.Equal(new[] { "23000000000010", "23000FFFF00010" }, reads);
        Assert.Single(ecu.Requests, r => r.Length == 5 && r[0] == 0x27);
        Assert.Empty(ecu.Programs);
        foreach (var request in ecu.Requests)
            DiagnosticProtocol.CheckReadOnly(request, 3968);
    }

    [Fact]
    public void Gen2ProbeUsesGen2SessionAndReadPackets()
    {
        var profile = Fixture.Profiles.Single(p => p.Calibration == "L3CGEE000");
        using var ecu = new TestEcu(Fixture.Image(profile)) { FamilyOverride = ReadFamily.Gen2 };
        var result = ReadProbe.Run(ecu);
        Assert.True(result.Passed);
        Assert.Equal(ReadFamily.Gen2, result.Family);
        Assert.Contains(ecu.Requests, r => Convert.ToHexString(r) == "1003");
        Assert.Contains(ecu.Requests, r => Convert.ToHexString(r) == "2324000FFFF00010");
        foreach (var request in ecu.Requests)
            DiagnosticProtocol.CheckReadOnly(request, 3968);
    }

    [Fact]
    public void RejectedUpperSampleIsReportedInsteadOfThrown()
    {
        using var ecu = new TestEcu(Fixture.Rom)
        {
            ModifyResponse = (request, response) => request[0] == 0x23 && request[2] == 0x0F ? throw new NegativeResponseException(0x23, 0x31) : response
        };
        var result = ReadProbe.Run(ecu);
        Assert.False(result.Passed);
        Assert.False(result.FullRomReadable);
        Assert.Contains("NRC 31", result.UpperReadError!);
    }

    [Fact]
    public void RunningEngineStopsBeforeTheReadSession()
    {
        using var ecu = new TestEcu(Fixture.Rom)
        {
            ModifyResponse = (request, response) => Convert.ToHexString(request) == "010C" ? new byte[] { 0x41, 0x0C, 0x0B, 0xB8 } : response
        };
        Assert.Throws<TransportException>(() => ReadProbe.Run(ecu));
        Assert.DoesNotContain(ecu.Requests, r => r[0] is 0x10 or 0x27 or 0x23);
    }
}
