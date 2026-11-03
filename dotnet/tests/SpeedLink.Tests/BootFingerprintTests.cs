using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class BootFingerprintTests
{
    private const int RecordStart = 0x7080, RecordLength = 128;
    private static RomProfile Profile(byte[] image)
    {
        var reference = Fixture.Profiles.Single(p => p.Calibration == "L3M7EF000");
        var boot = image[..reference.BootLength];
        boot.AsSpan(RecordStart, RecordLength).Clear();
        return reference with { BootDataRanges = [new(RecordStart, RecordLength)],
            BootSha256 = Convert.ToHexStringLower(SHA256.HashData(boot)) };
    }
    private static void SetSyntheticRecord(byte[] image, string identifier)
    {
        var record = image.AsSpan(RecordStart, RecordLength); record.Clear();
        Encoding.ASCII.GetBytes(identifier.PadRight(17)).CopyTo(record);
        ushort sum = 0;
        for (int i = 0; i < record.Length - 2; i += 2)
            sum = unchecked((ushort)(sum + BinaryPrimitives.ReadUInt16BigEndian(record.Slice(i, 2))));
        BinaryPrimitives.WriteUInt16BigEndian(record[^2..], unchecked((ushort)-sum));
    }

    [Fact]
    public void l3m6_profile_excludes_only_its_native_identification_record()
    {
        var profile = Fixture.Profiles.Single(p => p.Calibration == "L3M6EG000");
        var range = Assert.Single(profile.BootDataRanges!);
        Assert.Equal(RecordStart, range.Address); Assert.Equal(RecordLength, range.Length);
        var image = Fixture.Image(profile); SetSyntheticRecord(image, "GG3P123456");
        var inspector = new RomInspector([profile]);
        Assert.True(inspector.Inspect(image).ProfileValid);
        SetSyntheticRecord(image, "GG3P654321");
        Assert.True(inspector.Inspect(image).ProfileValid);
        image[RecordStart - 1] ^= 1;
        Assert.False(inspector.Inspect(image).CanExport);
    }

    [Fact]
    public void profiles_without_data_ranges_retain_exact_boot_verification()
    {
        foreach (var profile in Fixture.Profiles.Where(p => p.BootDataRanges is not { Length: > 0 }))
        {
            var image = Fixture.Image(profile);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(image.AsSpan(0, profile.BootLength))), RomInspector.BootHash(image, profile));
            image[0x100] ^= 1;
            Assert.False(new RomInspector([profile]).Inspect(image).BootRecognized);
        }
    }

    [Fact]
    public void per_ecu_data_changes_keep_recognition_but_adjacent_code_changes_are_rejected()
    {
        var first = Fixture.Image(Fixture.Profiles.Single(p => p.Calibration == "L3M7EF000"));
        SetSyntheticRecord(first, "GG3P123456"); var profile = Profile(first);
        var second = first.ToArray(); SetSyntheticRecord(second, "GG3P654321");
        var inspector = new RomInspector([profile]);
        Assert.NotEqual(RomInspector.Hash(first.AsSpan(0, profile.BootLength)), RomInspector.Hash(second.AsSpan(0, profile.BootLength)));
        Assert.Equal(RomInspector.BootHash(first, profile), RomInspector.BootHash(second, profile));
        Assert.True(inspector.Inspect(first).ProfileValid); Assert.True(inspector.Inspect(second).ProfileValid);
        foreach (int address in new[] { RecordStart - 1, RecordStart + RecordLength, 0x100, profile.BootLength - 1 })
        {
            var changed = second.ToArray(); changed[address] ^= 1;
            var result = inspector.Inspect(changed);
            Assert.False(result.BootRecognized); Assert.False(result.CanExport);
            Assert.Throws<InvalidDataException>(() => inspector.CorrectedCopy(changed));
        }
    }

    [Fact]
    public void hashing_and_all_editor_saves_preserve_the_complete_opaque_record()
    {
        using var folder = new TempFolder();
        var image = Fixture.Image(Fixture.Profiles.Single(p => p.Calibration == "L3M7EF000"));
        SetSyntheticRecord(image, "GG3P123456"); var original = image.ToArray(); var profile = Profile(image);
        var inspector = new RomInspector([profile]); var record = image.AsSpan(RecordStart, RecordLength).ToArray();
        _ = RomInspector.BootHash(image, profile); Assert.Equal(original, image);
        string source = Path.Combine(folder.Path, "original.bin"); File.WriteAllBytes(source, image);
        var editor = new RomEditor(image, source, inspector, Fixture.TuningGuard);
        string unchanged = Path.Combine(folder.Path, "unchanged.bin"); editor.SaveNew(unchanged);
        Assert.Equal(original, File.ReadAllBytes(unchanged));
        var table = editor.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float" && t.Address >= profile.ChecksumStart);
        editor.Apply([new(table, 0, table.Decode(123))], "Synthetic test edit");
        string edited = Path.Combine(folder.Path, "edited.bin"); editor.SaveNew(edited);
        var saved = File.ReadAllBytes(edited);
        Assert.Equal(record, saved.AsSpan(RecordStart, RecordLength).ToArray());
        Assert.True(inspector.Inspect(saved).ProfileValid); Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(-1, 128)]
    [InlineData(0x7080, 0)]
    [InlineData(0x7080, -1)]
    [InlineData(0x7FFF, 2)]
    [InlineData(0x8000, 1)]
    public void invalid_data_ranges_fail_closed(int address, int length)
    {
        var image = Fixture.Image(Fixture.Profiles.Single(p => p.Calibration == "L3M7EF000"));
        var profile = Profile(image) with { BootDataRanges = [new(address, length)] };
        Assert.Throws<InvalidDataException>(() => RomInspector.BootHash(image, profile));
    }

    [Fact]
    public void overlapping_data_ranges_fail_closed()
    {
        var image = Fixture.Image(Fixture.Profiles.Single(p => p.Calibration == "L3M7EF000"));
        var profile = Profile(image) with { BootDataRanges = [new(0x7080, 128), new(0x70F0, 32)] };
        Assert.Throws<InvalidDataException>(() => RomInspector.BootHash(image, profile));
    }
}
