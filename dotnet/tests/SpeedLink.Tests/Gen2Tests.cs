using System.Buffers.Binary;
using System.Text.Json;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class Gen2Tests
{
    private static RomProfile Profile => Fixture.Profiles.Single(p => p.Calibration == "L3CGEE000");
    private static byte[] Image() => Fixture.Image(Profile);
    [Theory]
    [InlineData("L3CGEE000")]
    [InlineData("L3CGEE000L3T6060")]
    public void IdentitySelectsGen2WithoutUsingGen1Packets(string calibration)
    {
        var protocol = ReadProtocol.For(new("test", "fixture", null, calibration));
        Assert.Equal(ReadFamily.Gen2, protocol.Family);
        Assert.Equal(3, protocol.Session); Assert.Equal(3, protocol.SeedLevel); Assert.Equal(4, protocol.KeyLevel);
        Assert.Equal(Convert.FromHexString("23240000000003FF"), protocol.ReadRequest(0, 1023));
        Assert.Equal(Convert.FromHexString("2324000FFFFF0001"), protocol.ReadRequest(0xFFFFF, 1));
        Assert.Equal(Convert.FromHexString("86CA06"), SecurityAccess.SeedKey(Convert.FromHexString("123456")));
        Assert.NotNull(LoggerDefinition.ForCalibration(calibration));
        Assert.Null(LoggerDefinition.ForCalibration(calibration + "X"));
    }
    [Theory]
    [InlineData("2324000000000000")]
    [InlineData("2324000000000400")]
    [InlineData("2324000FFFFF0002")]
    [InlineData("2324FFFFFFFF0001")]
    [InlineData("2324400000000001")]
    [InlineData("23140000000001")]
    [InlineData("2705")]
    [InlineData("2706000000")]
    [InlineData("1002")]
    [InlineData("3D004000000001AA")]
    [InlineData("3400440000000000100000")]
    public void ReadOnlyGateRejectsOtherLevelsRangesAndProgramming(string hex) =>
        Assert.Throws<TransportException>(() => DiagnosticProtocol.CheckReadOnly(Convert.FromHexString(hex), 3968));
    [Fact]
    public void AmbiguousIdentityCannotChooseAReaderAndUnknownBuildCannotChooseALogger()
    {
        Assert.Throws<TransportException>(() => ReadProtocol.For(new("test", "fixture", null, "L3CGEE000 L38KEH000")));
        var unknown = new EcuIdentity("test", "fixture", null, "L3CGEE000L3T9999");
        Assert.Null(LoggerDefinition.ForCalibration(unknown.Calibration));
        Assert.Equal(ReadFamily.Gen2, ReadProtocol.For(unknown, ReadFamily.Gen2).Family);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KnownAndUnknownGen2BackupsCoverBothPassesAndExactTail(bool unknown)
    {
        using var temp = new TempFolder(); byte[] input = unknown ? Enumerable.Repeat((byte)0xFF, RomInspector.RomSize).ToArray() : Image();
        using var peer = new TestEcu(input) { FamilyOverride = ReadFamily.Gen2, Seed = [0x12, 0x34, 0x56] };
        var progress = new ProgressList();
        var result = Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress, unknownFamily: ReadFamily.Gen2);
        var loaded = Fixture.Store().Load(result.Path);
        Assert.Equal(input, loaded.Data); Assert.Equal("Gen2", loaded.Manifest["identity"]!["readProtocol"]!.GetValue<string>());
        Assert.Equal(!unknown, result.Inspection.ProfileRecognized);
        if (unknown)
        {
            Assert.Equal("unavailable", result.Inspection.IntegrityPolicy);
            Assert.False(result.Inspection.CanExport); Assert.False(result.Inspection.MarkersValid);
        }
        Assert.Contains(peer.Requests, r => r.AsSpan().SequenceEqual(Convert.FromHexString("270486CA06")));
        Assert.DoesNotContain(peer.Requests, r => r.AsSpan().SequenceEqual(Convert.FromHexString("1087")));
        int total = 0; int cursor = 0; int pass = 1;
        foreach (var r in peer.Requests.Where(r => r[0] == 0x23))
        {
            DiagnosticProtocol.CheckReadOnly(r, 3968);
            Assert.Equal(8, r.Length); Assert.Equal(0x24, r[1]);
            int address = BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(2, 4)), count = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6, 2));
            Assert.Equal(cursor, address); Assert.InRange(count, 1, 1023); cursor += count; total += count;
            if (cursor == RomInspector.RomSize) { cursor = 0; pass++; }
        }
        Assert.Equal(3, pass); Assert.Equal(2 * RomInspector.RomSize, total);
        Assert.Equal(BackupReadStage.Complete, progress.Items[^1].Stage);
        Assert.True(File.GetAttributes(result.Path).HasFlag(FileAttributes.ReadOnly));
    }
    [Theory]
    [InlineData("short")] [InlineData("long")] [InlineData("wrong")] [InlineData("different")] [InlineData("cancel")]
    public void FailedOrCancelledReadNeverSealsVerifiedBackupOrRetries(string fault)
    {
        using var temp = new TempFolder(); using var cancellation = new CancellationTokenSource();
        using var peer = new TestEcu(Image()); int reads = 0;
        peer.ModifyResponse = (request, response) =>
        {
            if (request[0] != 0x23) return response;
            reads++;
            if (fault == "cancel") { cancellation.Cancel(); return response; }
            if (fault == "different") { if (reads > 1026) response[1] ^= 1; return response; }
            return fault switch { "short" => response[..^1], "long" => [.. response, 0], "wrong" => [0x62, .. response[1..]], _ => response };
        };
        Assert.ThrowsAny<Exception>(() => Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, cancellation: cancellation.Token));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories), p => Path.GetFileName(p) == "rom.bin");
        Assert.False(File.Exists(Path.Combine(temp.Path, "backup.bin")));
        if (fault != "different") Assert.Equal(1, reads);
        Assert.Contains(Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories), p => Path.GetExtension(p) == ".read");
    }
    [Fact]
    public void OpenLoopPartThrottleFuelTargetsUseNativeLambdaUnits()
    {
        var definition = TuningDefinition.Known.Single(d => d.Calibration == Profile.Calibration);
        var targets = definition.Tables.Where(t => t.Name.StartsWith("Open Loop Part Throttle AFR Target", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, targets.Length);
        Assert.All(targets, target =>
        {
            Assert.Equal("lambda", target.Units);
            Assert.Equal(0.8, target.Encode(0.8));
        });
    }

    [Fact]
    public void NativeIntegrityExcludesCalibrationAndGuardsCodeCorrection()
    {
        var inspector = Fixture.Inspector(); byte[] image = Image(); var info = inspector.Inspect(image);
        Assert.True(info.ProfileValid); Assert.True(info.CanExport); Assert.True(info.CanCorrectChecksum);
        Assert.Equal(0x3FFC0, info.CorrectionAddress);
        Assert.Equal(image, inspector.CorrectedCopy(image));
        foreach (int at in new[] { 0xC0000, 0xF0000, 0x1C000 })
        {
            var copy = image.ToArray(); copy[at] ^= 1; Assert.True(inspector.Inspect(copy).ChecksumValid);
        }
        foreach (int at in new[] { 0x4000, 0x1BFFE, 0x20000, 0xFFFFE, 0x80000, 0x12D68 })
        {
            var copy = image.ToArray(); copy[at] ^= 1;
            Assert.Throws<InvalidDataException>(() => inspector.CorrectedCopy(copy)); Assert.Equal(image[at] ^ 1, copy[at]);
        }
    }

    [Theory]
    [InlineData(0x40314)] [InlineData(0x40310)] [InlineData(0x4030C)]
    [InlineData(0x44358)] [InlineData(0x44354)] [InlineData(0x44350)]
    public void MapScalingEditsSaveAndReopenWithOnlyNativeCorrectionWordChanged(int address)
    {
        using var temp = new TempFolder();
        var image = Image(); var inspector = Fixture.Inspector();
        string source = Path.Combine(temp.Path, "source.bin"), savedPath = Path.Combine(temp.Path, "edited.bin");
        File.WriteAllBytes(source, image);
        var editor = new RomEditor(image, source, inspector, Fixture.TuningGuard);
        var control = editor.Definition.Tables.Single(t => t.Address == address);
        Assert.StartsWith("MAP Scale", control.Name);
        editor.Apply([new(control, 0, 12.5)], "Change MAP scaling");
        var edited = editor.Snapshot();
        Assert.False(inspector.Inspect(edited).ChecksumValid);
        editor.SaveNew(savedPath);
        var saved = File.ReadAllBytes(savedPath);
        Assert.True(inspector.Inspect(saved).ProfileValid);
        Assert.NotEqual(edited.AsSpan(0x3FFC0, 4).ToArray(), saved.AsSpan(0x3FFC0, 4).ToArray());
        Assert.Equal(edited.AsSpan(0, 0x3FFC0).ToArray(), saved.AsSpan(0, 0x3FFC0).ToArray());
        Assert.Equal(edited.AsSpan(0x3FFC4).ToArray(), saved.AsSpan(0x3FFC4).ToArray());
        Assert.Equal(12.5, new RomEditor(saved, savedPath, inspector, Fixture.TuningGuard).Read(control, 0));
        Assert.Equal(image, File.ReadAllBytes(source));
        Assert.Equal(saved, inspector.CorrectedCopy(saved));
    }
    [Theory]
    [InlineData("5003")]
    [InlineData("5003003201F400")]
    [InlineData("5087003201F4")]
    public void Gen2SessionAcknowledgementRequiresNativeTimingPayload(string reply)
    {
        using var temp = new TempFolder(); using var peer = new TestEcu(Image());
        peer.ModifyResponse = (request, response) => request[0] == 0x10 ? Convert.FromHexString(reply) : response;
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path));
        Assert.DoesNotContain(peer.Requests, r => r[0] is 0x27 or 0x23);
    }
    [Fact]
    public void EveryIncludedControlEditsOnlyItsStorage()
    {
        using var temp = new TempFolder(); var image = Image();
        var inspector = Fixture.Inspector();
        var editor = new RomEditor(image, Path.Combine(temp.Path, "original.bin"), inspector, Fixture.TuningGuard);
        Assert.Equal(448, editor.Definition.Tables.Count);
        foreach (var table in editor.Definition.Tables)
        {
            double value = table.IsChoice ? table.States[^1].Value : table.Decode(1);
            editor.Apply([new(table, 0, value)], table.Name); var edited = editor.Snapshot();
            Assert.True(image.AsSpan(0, table.Address).SequenceEqual(edited.AsSpan(0, table.Address)));
            Assert.True(image.AsSpan(table.End).SequenceEqual(edited.AsSpan(table.End)));
            Assert.Equal(table.Address >= 0xC0000, inspector.Inspect(edited).ProfileValid);
            editor.Undo(); Assert.Equal(image, editor.Snapshot());
        }
        var first = editor.Definition.Tables.First(t => !t.IsChoice); editor.Apply([new(first, 0, first.Decode(2))], "Edit");
        string path = Path.Combine(temp.Path, "edited.bin"); editor.SaveNew(path);
        Assert.Equal(editor.Snapshot(), File.ReadAllBytes(path)); Assert.True(inspector.Inspect(File.ReadAllBytes(path)).ProfileValid);
    }
    private sealed class ProgressList : IProgress<ReadProgress>
    { public List<ReadProgress> Items { get; } = []; public void Report(ReadProgress value) => Items.Add(value); }
}
