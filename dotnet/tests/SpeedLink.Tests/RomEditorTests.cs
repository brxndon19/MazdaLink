using System.Buffers.Binary;
using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class RomEditorTests
{
    public static IEnumerable<object[]> Profiles => Fixture.Profiles.Where(p => p.Family == "Gen1").Select(p => new object[] { p.Calibration });
    private static RomEditor Open(string calibration)
    {
        var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        return new(Fixture.Image(profile), Path.Combine(Path.GetTempPath(), "ORIGINAL_" + calibration + ".bin"), Fixture.Inspector(), Fixture.TuningGuard);
    }
    [Fact]
    public void DefinitionsAreCompleteAndNaturallySorted()
    {
        Assert.Equal(10, TuningDefinition.Known.Count);
        Assert.Equal(8411, TuningDefinition.Known.Sum(d => d.Tables.Count));
        foreach (var d in TuningDefinition.Known)
        {
            Assert.Equal(d.Calibration switch { "L3CGEE000" => 450, "L3M6EG000" or "L3M7EF000" => 865, "L3P4EM000" => 897, _ => 889 }, d.Tables.Count);
            Assert.All(d.Tables, t => { Assert.Equal(t.Columns, t.X.Values.Length); Assert.Equal(t.Rows, t.Y.Values.Length); Assert.InRange(t.End, 0x8000, RomInspector.RomSize - 2); });
            foreach (var group in d.Tables.GroupBy(t => t.Category)) Assert.Equal(group.OrderBy(t => t.Name, NaturalOrder.Instance), group);
        }
        Assert.True(NaturalOrder.Instance.Compare("Map 2", "Map 10") < 0);
    }
    [Theory, MemberData(nameof(Profiles))]
    public void NumericEditIsLocalizedAtomicAndUndoRestoresExactBytes(string calibration)
    {
        var doc = Open(calibration); var table = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        byte[] before = doc.Snapshot(); doc.Apply([new(table, 0, 1234.5)], "Scalar edit"); byte[] after = doc.Snapshot();
        Assert.True(doc.IsDirty); Assert.Equal(1234.5, doc.Read(table, 0), 4);
        Assert.All(Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]), i => Assert.InRange(i, table.Address, table.End - 1));
        var bit = doc.Definition.Tables.First(t => t.Bit is not null);
        Assert.Throws<ArgumentException>(() => doc.Apply([new(table, 0, 2222), new(bit, 0, 2)], "Invalid transaction")); Assert.Equal(after, doc.Snapshot());
        doc.Undo(); Assert.Equal(before, doc.Snapshot()); Assert.False(doc.IsDirty); Assert.True(doc.CanRedo);
        doc.Redo(); Assert.Equal(after, doc.Snapshot()); doc.Undo(); doc.Apply([new(table, 0, 1500)], "New edit"); Assert.False(doc.CanRedo);
    }
    [Theory, MemberData(nameof(Profiles))]
    public void EveryDtcBitPreservesAllOtherBitsAndAliases(string calibration)
    {
        var doc = Open(calibration);
        foreach (var table in doc.Definition.Tables.Where(t => t.Bit is not null))
        {
            byte[] before = doc.Snapshot(); var original = Enumerable.Range(0, table.Count).Select(i => doc.Read(table, i)).ToArray(); doc.Apply(original.Select((v, i) => new CellEdit(table, i, 1 - v)), "Toggle all named bits"); byte[] after = doc.Snapshot();
            uint a = table.Width == 2 ? BinaryPrimitives.ReadUInt16BigEndian(before.AsSpan(table.Address, 2)) : before[table.Address];
            uint b = table.Width == 2 ? BinaryPrimitives.ReadUInt16BigEndian(after.AsSpan(table.Address, 2)) : after[table.Address];
            Assert.Equal(table.Bits.Aggregate(0u, (mask, bit) => mask | (1u << bit)), a ^ b);
            for (int i = 0; i < table.Count; i++) Assert.Equal(1 - original[i], doc.Read(table, i));
            Assert.Equal(before.AsSpan(0, table.Address).ToArray(), after.AsSpan(0, table.Address).ToArray());
            Assert.Equal(before.AsSpan(table.End).ToArray(), after.AsSpan(table.End).ToArray());
            foreach (var alias in doc.Definition.Tables.Where(t => t.Address == table.Address && t.Bit is not null))
                foreach (int i in Enumerable.Range(0, alias.Count).Where(i => !table.Bits.Contains(alias.Bits[i]))) Assert.False(doc.CellChanged(alias, i));
            doc.Reset(table, Enumerable.Range(0, table.Count), TuningBaseline.Original); Assert.Equal(before, doc.Snapshot());
        }
    }
    [Theory, MemberData(nameof(Profiles))]
    public void NamedStatesAndUnsignedScaledStorageAreEnforced(string calibration)
    {
        var doc = Open(calibration);
        foreach (var table in doc.Definition.Tables.Where(t => t.Kind == "Switch"))
        {
            foreach (var state in table.States) { doc.Apply([new(table, 0, state.Value)], "State"); Assert.Equal(state.Value, doc.Read(table, 0)); }
            Assert.Throws<ArgumentException>(() => doc.Apply([new(table, 0, 255)], "Invalid state"));
        }
        foreach (var table in doc.Definition.Tables.Where(t => !t.IsChoice && t.Storage != "float"))
        {
            double value = table.Decode(100); doc.Apply([new(table, 0, value)], "Scale"); Assert.Equal(value, doc.Read(table, 0), 8);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Apply([new(table, 0, table.Decode(10000000000))], "Overflow"));
        }
    }
    [Theory, MemberData(nameof(Profiles))]
    public void SaveCreatesVerifiedCorrectedCopyAndPreservesOriginalAndHistory(string calibration)
    {
        using var folder = new TempFolder(); var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        string source = Path.Combine(folder.Path, "original.bin"); var input = Fixture.Image(profile); File.WriteAllBytes(source, input);
        var doc = new RomEditor(input, source, Fixture.Inspector(), Fixture.TuningGuard); string noEdit = Path.Combine(folder.Path, "no-edit.bin"); doc.SaveNew(noEdit);
        Assert.Equal(input, File.ReadAllBytes(noEdit)); Assert.False(doc.IsDirty);
        var table = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float"); doc.Apply([new(table, 0, 1456)], "Change");
        var beforeSave = doc.Snapshot(); string edited = Path.Combine(folder.Path, "edited.bin"); doc.SaveNew(edited);
        var output = File.ReadAllBytes(edited); Assert.True(Fixture.Inspector().Inspect(output).ChecksumValid); Assert.Equal(input, File.ReadAllBytes(source)); Assert.False(doc.IsDirty);
        Assert.All(Enumerable.Range(0, output.Length).Where(i => beforeSave[i] != output[i]), i => Assert.InRange(i, profile.CorrectionAddress, profile.CorrectionAddress + 3));
        Assert.Throws<IOException>(() => doc.SaveNew(source)); Assert.Throws<IOException>(() => doc.SaveNew(edited));
        Assert.Equal(output, File.ReadAllBytes(edited)); Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.partial"));
        while (doc.CanUndo) doc.Undo(); Assert.Equal(input, doc.Snapshot());
    }
    [Theory, MemberData(nameof(Profiles))]
    public void SaveAlwaysRepairsChecksumEvenWithoutTableEdits(string calibration)
    {
        using var folder = new TempFolder(); var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var input = Fixture.Image(profile); input[profile.CorrectionAddress] ^= 0x01;
        string source = Path.Combine(folder.Path, "bad-checksum.bin"); File.WriteAllBytes(source, input);
        var inspector = Fixture.Inspector(); Assert.False(inspector.Inspect(input).ChecksumValid);
        var editor = new RomEditor(input, source, inspector, Fixture.TuningGuard);
        Assert.False(editor.IsDirty); string saved = Path.Combine(folder.Path, "saved.bin"); editor.SaveNew(saved);
        var output = File.ReadAllBytes(saved); Assert.True(inspector.Inspect(output).ChecksumValid);
        Assert.Equal(input, File.ReadAllBytes(source)); Assert.False(editor.IsDirty);
        Assert.All(Enumerable.Range(0, input.Length).Where(i => input[i] != output[i]), i => Assert.InRange(i, profile.CorrectionAddress, profile.CorrectionAddress + 3));
        Assert.Equal(output, new RomEditor(output, saved, inspector, Fixture.TuningGuard).Snapshot());
    }
    [Theory, MemberData(nameof(Profiles))]
    public void ClipboardInterpolationComparisonAndResetUsePhysicalAxes(string calibration)
    {
        var doc = Open(calibration); var table = doc.Definition.Tables.First(t => t.Kind == "3D" && t.Storage == "float");
        var before = doc.Snapshot(); int right = Math.Min(table.Columns - 1, 3), bottom = Math.Min(table.Rows - 1, 3);
        doc.Apply([new(table, 0, 10), new(table, right, 30), new(table, bottom * table.Columns, 50), new(table, bottom * table.Columns + right, 90)], "Corners");
        doc.Interpolate(table, 0, 0, right, bottom, "2D");
        double x = (table.X.Values[1] - table.X.Values[0]) / (table.X.Values[right] - table.X.Values[0]), y = (table.Y.Values[1] - table.Y.Values[0]) / (table.Y.Values[bottom] - table.Y.Values[0]);
        Assert.Equal((10 * (1 - x) + 30 * x) * (1 - y) + (50 * (1 - x) + 90 * x) * y, doc.Read(table, table.Columns + 1), 4);
        doc.Paste(table, 0, 0, "11\t12\r\n21\t22\r\n"); Assert.Equal(22, doc.Read(table, table.Columns + 1), 4);
        byte[] staged = doc.Snapshot(); Assert.Throws<InvalidDataException>(() => doc.Paste(table, 0, 0, "1\tNaN")); Assert.Equal(staged, doc.Snapshot());
        Assert.Throws<ArgumentException>(() => doc.Paste(table, table.Rows - 1, table.Columns - 1, "1\t2")); Assert.Equal(staged, doc.Snapshot());
        doc.LoadComparison(before, "comparison.bin"); Assert.True(doc.TableChanged(table, TuningBaseline.Comparison));
        doc.Reset(table, Enumerable.Range(0, table.Count), TuningBaseline.Comparison); Assert.Equal(before, doc.Snapshot());
        var wrong = (byte[])before.Clone(); wrong[doc.Definition.IdentityAddress] ^= 1; Assert.Throws<InvalidDataException>(() => doc.LoadComparison(wrong, "wrong.bin"));
    }
    [Fact]
    public void ProgramSignaturesCoverEveryKnownDefinition()
    {
        Assert.Equal(TuningDefinition.Known.Select(d => d.Calibration).Order(), TuningCodeGuard.Known.Select(s => s.Calibration).Order());
        foreach (var signature in TuningCodeGuard.Known)
            Assert.Equal(RomInspector.KnownProfiles.Single(p => p.Calibration == signature.Calibration).ChecksumStart, signature.CodeStart);
    }
    [Theory, MemberData(nameof(Profiles))]
    public void ProgramChangesWithValidIdentityBootAndChecksumAreRejected(string calibration)
    {
        var doc = Open(calibration); var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var patched = doc.Snapshot(); patched[0xF0000] ^= 1; patched = Fixture.Inspector().CorrectedCopy(patched);
        Assert.True(Fixture.Inspector().Inspect(patched).ProfileValid);
        Assert.Throws<InvalidDataException>(() => new RomEditor(patched, "patched.bin", Fixture.Inspector(), Fixture.TuningGuard));
        Assert.Throws<InvalidDataException>(() => doc.LoadComparison(patched, "patched.bin"));
        var table = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        doc.Apply([new(table, 0, 8123)], "Allowed calibration edit");
        var reopened = new RomEditor(doc.Snapshot(), "edited.bin", Fixture.Inspector(), Fixture.TuningGuard);
        Assert.Equal(8123, reopened.Read(reopened.Definition.Tables.Single(t => t.Name == table.Name), 0));
    }
    [Fact]
    public void InvalidImagesAndUnencodableValuesAreRejectedWithoutMutation()
    {
        var doc = Open("L38KEH000"); var bytes = doc.Snapshot(); var invalid = (byte[])bytes.Clone(); invalid[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => new RomEditor(invalid, "wrong-boot.bin", Fixture.Inspector()));
        Assert.Throws<InvalidDataException>(() => new RomEditor(bytes[..^1], "short.bin", Fixture.Inspector()));
        var scalar = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        Assert.Throws<ArgumentException>(() => doc.Apply([new(scalar, 0, double.NaN)], "NaN"));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Apply([new(scalar, 0, double.MaxValue)], "Overflow"));
        var packed = doc.Definition.Tables.Single(t => t.Name == "Fuel Pressure High Limit Monitor Margin");
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Apply([new(packed, 0, -1)], "Negative packed threshold"));
        Assert.Equal(bytes, doc.Snapshot()); Assert.False(doc.CanUndo);
    }
    [Fact]
    public void XmlRejectsExternalEntitiesAndUnknownEncoding()
    {
        using var dtd = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE roms [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><roms>&x;</roms>"));
        Assert.Throws<System.Xml.XmlException>(() => TuningDefinition.Parse(dtd));
        using var xml = new MemoryStream(Encoding.UTF8.GetBytes("<roms><rom><romid/><table type='Executable'/></rom></roms>"));
        Assert.Throws<InvalidDataException>(() => TuningDefinition.Parse(xml));
        Assert.Throws<InvalidDataException>(() => RomEditor.ParseClipboard("1\t2\n3"));
    }
}
