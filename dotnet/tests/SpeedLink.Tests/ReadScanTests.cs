using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class ReadScanTests
{
    // Seed and key from a real ZY87EC000 (Mazda 2, ZY-VE) Gen 2 read session that the ECU accepted.
    private static TestEcu Mazda2(Func<int, int, bool> rejects) => new(Fixture.Rom)
    {
        ReportedCalibration = "UNRECOGNIZED",
        FamilyOverride = ReadFamily.Gen2,
        Seed = [0x1C, 0x2C, 0x75],
        ModifyResponse = (request, response) =>
        {
            if (request[0] != 0x23)
                return response;
            int address = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(2, 4));
            int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(6, 2));
            return rejects(address, count) ? throw new NegativeResponseException(0x23, 0x31) : response;
        }
    };

    [Fact]
    public void FindsTheLargestAcceptedBlockWithOneKeyAttempt()
    {
        using var ecu = Mazda2((_, count) => count > 255);
        var result = ReadScan.Run(ecu, ReadFamily.Gen2);
        Assert.Equal(ReadFamily.Gen2, result.Family);
        Assert.Equal(255, result.LargestBlockAtStart);
        Assert.All(result.Attempts.Where(a => a.Address != 0), a => Assert.True(a.Accepted));
        Assert.Equal("NRC 31", result.Attempts.Single(a => a.Address == 0 && a.Count == 1023).Outcome);
        Assert.Equal(ReadScan.BlockSizes.Length + ReadScan.Addresses.Length, result.Attempts.Count);
        Assert.Single(ecu.Requests, r => r.Length == 5 && r[0] == 0x27);
        Assert.Contains(ecu.Requests, r => Convert.ToHexString(r) == "2704F9624C");
        Assert.Empty(ecu.Programs);
        foreach (var request in ecu.Requests)
            DiagnosticProtocol.CheckReadOnly(request, 3968);
    }

    [Theory]
    [InlineData("ZY87EC000")]
    [InlineData("ZY87EC000ZYV5050")]
    public void Mazda2AutomaticallyUsesObservedSessionAnd512ByteBlocks(string calibration)
    {
        using var ecu = Mazda2((_, count) => count > 512);
        ecu.ReportedCalibration = calibration;
        using var vault = new TempFolder();
        var identity = ReadWorkflow.Identify(ecu);
        var result = new ReadWorkflow().ReadBackup(ecu, identity, vault.Path);
        Assert.Equal(RomInspector.Hash(Fixture.Rom), result.Inspection.Sha256);
        var reads = ecu.Requests.Where(r => r[0] == 0x23).ToArray();
        Assert.Equal(4096, reads.Length); // two complete 1 MiB passes
        Assert.All(reads, r => Assert.Equal(512, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6))));
        Assert.DoesNotContain(ecu.Requests, r => Convert.ToHexString(r) == "1087");
        Assert.Single(ecu.Requests, r => r.Length == 5 && r[0] == 0x27);
        Assert.Contains(ecu.Requests, r => Convert.ToHexString(r) == "2704F9624C");
        Assert.Empty(ecu.Programs);
    }

    [Theory]
    [InlineData("ZY87EC000X")]
    [InlineData("ZY87EC000ZYV9999")]
    public void UnverifiedMazda2IdentifiersDoNotAutomaticallySelectAProtocol(string calibration)
    {
        Assert.False(ReadProtocol.IsMazda2(calibration));
        Assert.Equal(ReadFamily.Gen1, ReadProtocol.For(new("test", "fixture", null, calibration)).Family);
    }

    [Fact]
    public void ReportsProtectedStartWhenOnlyHigherAddressesRead()
    {
        using var ecu = Mazda2((address, _) => address < 0x4000);
        var result = ReadScan.Run(ecu, ReadFamily.Gen2);
        Assert.Null(result.LargestBlockAtStart);
        Assert.True(result.AnyAccepted);
        Assert.False(result.Attempts.Single(a => a.Address == 0x2000).Accepted);
        Assert.True(result.Attempts.Single(a => a.Address == 0x4000).Accepted);
    }

    [Fact]
    public void KeepsGoingWhenEveryReadIsRefused()
    {
        using var ecu = Mazda2((_, _) => true);
        var result = ReadScan.Run(ecu, ReadFamily.Gen2);
        Assert.False(result.AnyAccepted);
        Assert.All(result.Attempts, a => Assert.Equal("NRC 31", a.Outcome));
    }

    [Fact]
    public void SmallerBlockLimitLetsTheFullReadComplete()
    {
        using var ecu = Mazda2((_, count) => count > 255);
        using var vault = new TempFolder();
        var identity = ReadWorkflow.Identify(ecu);
        var backup = new ReadWorkflow().ReadBackup(ecu, identity, vault.Path, unknownFamily: ReadFamily.Gen2, maximumBlock: 255);
        Assert.Equal(RomInspector.Hash(Fixture.Rom), backup.Inspection.Sha256);
        Assert.All(ecu.Requests.Where(r => r[0] == 0x23), r => Assert.True(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6, 2)) <= 255));
    }
}
