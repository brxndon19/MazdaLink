using System.Buffers.Binary;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class RomSaveIntegrityTests
{
    public static IEnumerable<object[]> AllEditorProfiles => TuningDefinition.Known
        .OrderBy(d => d.Calibration).Select(d => new object[] { d.Calibration });

    // Independent sum: fixed, established family bounds; never calls the production sum helper.
    private static uint Sum(byte[] bytes, RomProfile profile)
    {
        Assert.Equal(RomInspector.RomSize, bytes.Length);
        Assert.Contains(profile.Family, new[] { "Gen1", "Gen2" });
        int first = profile.Calibration switch
        {
            "L3CGEE000" => 0x4000,
            "L3M6EG000" or "L3M7EF000" => 0x3F000,
            "L33XEH000" or "L35HEH000" or "L38KEH000" or "L39SED000" or "L39SEE000" or "L39SEF000" or "L3P4EM000" => 0x48000,
            _ => throw new InvalidDataException("Establish an independent checksum range for the new ROM profile.")
        };
        Assert.Equal(first, profile.ChecksumStart);
        uint sum = 0;
        for (int at = first; at < 0x100000; at += 4)
        {
            if (profile.Family == "Gen2" && (at >= 0xC0000 || at >= 0x1C000 && at < 0x20000)) continue;
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4)));
        }
        return sum;
    }

    [Fact]
    public void SaveIntegrityMatrixIncludesEverySupportedEditorDefinition()
    {
        Assert.Equal(TuningDefinition.Known.Select(d => d.Calibration).Order(),
            Fixture.Profiles.Select(p => p.Calibration).Order());
        Assert.Equal(TuningDefinition.Known.Count, AllEditorProfiles.Count());
    }

    [Theory, MemberData(nameof(AllEditorProfiles))]
    public void EveryEditedAndRepeatedSaveHasValidChecksumAndPreservesSource(string calibration)
    {
        using var folder = new TempFolder();
        var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var original = Fixture.Image(profile); string source = Path.Combine(folder.Path, "original.bin");
        File.WriteAllBytes(source, original); Assert.Equal(0x5AA55AA5u, Sum(original, profile));
        var inspector = Fixture.Inspector(); var editor = new RomEditor(original, source, inspector, Fixture.TuningGuard);
        var table = editor.Definition.Tables.First(t => !t.IsChoice && t.Storage == "float" && t.Address % 4 == 0 && t.Address >= profile.ChecksumStart);
        for (int pass = 1; pass <= 2; pass++)
        {
            double value = table.Decode(pass);
            if (value == editor.Read(table, 0)) value = table.Decode(pass + 10);
            editor.Apply([new(table, 0, value)], "Checksum contract edit");
            var edited = editor.Snapshot();
            Assert.Equal(profile.Family == "Gen2", Sum(edited, profile) == 0x5AA55AA5u);
            string path = Path.Combine(folder.Path, $"edited-{pass}.bin"); editor.SaveNew(path);
            var saved = File.ReadAllBytes(path);
            Assert.Equal(0x5AA55AA5u, Sum(saved, profile)); Assert.True(inspector.Inspect(saved).ProfileValid);
            Assert.Equal(edited.AsSpan(table.Address, table.Width).ToArray(), saved.AsSpan(table.Address, table.Width).ToArray());
            if (profile.CorrectionAddress >= 0)
                Assert.All(Enumerable.Range(0, saved.Length).Where(i => saved[i] != edited[i]),
                    i => Assert.InRange(i, profile.CorrectionAddress, profile.CorrectionAddress + 3));
            else Assert.Equal(edited, saved);
            Assert.Equal(saved, new RomEditor(saved, path, inspector, Fixture.TuningGuard).Snapshot());
            Assert.False(editor.IsDirty); Assert.Equal(original, File.ReadAllBytes(source));
        }
        var before = editor.Snapshot(); string noEdit = Path.Combine(folder.Path, "no-edit.bin"); editor.SaveNew(noEdit);
        Assert.Equal(before, File.ReadAllBytes(noEdit)); Assert.Equal(0x5AA55AA5u, Sum(File.ReadAllBytes(noEdit), profile));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.partial"));
    }

    [Theory, MemberData(nameof(AllEditorProfiles))]
    public void NoEditSaveCannotBypassChecksumCorrectionOrNativeRejection(string calibration)
    {
        using var folder = new TempFolder(); var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var input = Fixture.Image(profile); var inspector = Fixture.Inspector();
        if (profile.CorrectionAddress >= 0) input[profile.CorrectionAddress] ^= 1;
        else input[0x80000] ^= 1;
        Assert.NotEqual(0x5AA55AA5u, Sum(input, profile));
        string source = Path.Combine(folder.Path, "invalid-checksum.bin"), output = Path.Combine(folder.Path, "export.bin");
        File.WriteAllBytes(source, input);
        if (profile.CorrectionAddress >= 0)
        {
            var editor = new RomEditor(input, source, inspector, Fixture.TuningGuard); Assert.False(editor.IsDirty);
            editor.SaveNew(output); var saved = File.ReadAllBytes(output);
            Assert.Equal(0x5AA55AA5u, Sum(saved, profile)); Assert.True(inspector.Inspect(saved).ProfileValid);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => new RomEditor(input, source, inspector, Fixture.TuningGuard).SaveNew(output));
            Assert.Throws<InvalidDataException>(() => inspector.CorrectedCopy(input));
            Assert.False(File.Exists(output));
        }
        Assert.Equal(input, File.ReadAllBytes(source)); Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.partial"));
    }
}
