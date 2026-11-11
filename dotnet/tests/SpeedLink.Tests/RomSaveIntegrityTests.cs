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

    [Theory]
    [InlineData("L33XEH000", 0xd008, 0xcf98, 0xd220, 0xd1b0)]
    [InlineData("L35HEH000", 0xd008, 0xcf98, 0xd220, 0xd1b0)]
    [InlineData("L38KEH000", 0xce40, 0xcdd0, 0xd058, 0xcfe8)]
    [InlineData("L39SED000", 0xd060, 0xcff0, 0xd278, 0xd208)]
    [InlineData("L39SEE000", 0xd060, 0xcff0, 0xd278, 0xd208)]
    [InlineData("L39SEF000", 0xd1a0, 0xd130, 0xd3b8, 0xd348)]
    [InlineData("L3M6EG000", 0xcc8c, 0xcc1c, 0xcea4, 0xce34)]
    [InlineData("L3M7EF000", 0xccc8, 0xcc58, 0xcee0, 0xce70)]
    [InlineData("L3P4EM000", 0xcd58, 0xcce8, 0xcf70, 0xcf00)]
    [InlineData("L3CGEE000", 0xd76a4, 0xd7634, 0xd7c4c, 0xd7bdc)]
    public void FuelFeedbackEditsKeepFrontAndRearCurvesSeparate(string calibration, int frontGain, int frontPeriod, int rearGain, int rearPeriod)
    {
        using var folder = new TempFolder();
        var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var original = Fixture.Image(profile);
        var source = Path.Combine(folder.Path, "original.bin");
        File.WriteAllBytes(source, original);
        var inspector = Fixture.Inspector();
        var editor = new RomEditor(original, source, inspector, Fixture.TuningGuard);
        var curves = new[]
        {
            (Name: "Primary Wideband Gain", Address: frontGain, Unit: "Ratio", Value: 1.25f),
            (Name: "Primary Wideband Response Period", Address: frontPeriod, Unit: "s", Value: 0.5f),
            (Name: "Rear O2 Gain", Address: rearGain, Unit: "Ratio", Value: 0.75f),
            (Name: "Rear O2 Response Period", Address: rearPeriod, Unit: "s", Value: 1.5f)
        };
        const int cell = 5;
        var expected = (byte[])original.Clone();
        var edits = new List<CellEdit>();
        foreach (var curve in curves)
        {
            var map = editor.Definition.Tables.Single(t => t.Name == "Fuel Feedback - " + curve.Name);
            Assert.Equal(curve.Address, map.Address);
            Assert.Equal(curve.Unit, map.Units);
            Assert.Equal((28, 1, "float", true), (map.Columns, map.Rows, map.Storage, map.BigEndian));
            Assert.Equal("Airflow (g/s)", map.X.Name);
            edits.Add(new(map, cell, curve.Value));
            BinaryPrimitives.WriteSingleBigEndian(expected.AsSpan(curve.Address + cell * 4, 4), curve.Value);
        }
        editor.Apply(edits, "Edit front and rear fuel feedback curves");
        string path = Path.Combine(folder.Path, "fuel-feedback.bin");
        editor.SaveNew(path);
        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(expected, saved);
        Assert.Equal(0x5AA55AA5u, Sum(saved, profile));
        var reopened = new RomEditor(saved, path, inspector, Fixture.TuningGuard);
        foreach (var edit in edits)
            Assert.Equal(edit.Value, reopened.Read(reopened.Definition.Tables.Single(t => t.Name == edit.Table.Name), cell));
        Assert.Equal(original, File.ReadAllBytes(source));
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
