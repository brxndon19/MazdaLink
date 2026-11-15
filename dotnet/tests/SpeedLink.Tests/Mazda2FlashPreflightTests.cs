using System.Buffers.Binary;
using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class Mazda2FlashPreflightTests
{
    private static string CreateVerifiedBackup(string folder, byte[] rom)
    {
        var store = new BackupStore();
        var (operationFolder, journal) = store.CreateOperation(folder, "hardware", "read_backup");
        var hash = RomInspector.Hash(rom);
        File.WriteAllBytes(Path.Combine(operationFolder, "pass1.read"), rom);
        File.WriteAllBytes(Path.Combine(operationFolder, "pass2.read"), rom);
        journal.Append("completed", new() { ["sha256"] = hash });
        journal.Dispose();
        var identity = new EcuIdentity("hardware", "test", "TESTVIN00000000000", "ZY87EC000ZYV5050");
        store.Seal(operationFolder, rom, identity, new RomInspector().Inspect(rom), hash, hash, "hardware");
        return Path.Combine(operationFolder, "rom.bin");
    }

    private static void Cleanup(string folder)
    {
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }

    private static byte[] Synthetic(int seed = 87)
    {
        var rom = new byte[RomInspector.RomSize];
        new Random(seed).NextBytes(rom);
        Encoding.ASCII.GetBytes(Mazda2FlashPreflight.SoftwareId).CopyTo(rom, Zy87Integrity.IdentityOffset);
        rom.AsSpan(Zy87Integrity.TableAddress, 0x0FF800 - Zy87Integrity.TableAddress).Fill(0xFF);
        for (int index = 0; index < Zy87Integrity.Ranges.Length; index++)
        {
            int entry = Zy87Integrity.TableAddress + index * 12;
            BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(entry), (uint)Zy87Integrity.Ranges[index].Start);
            BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(entry + 4), (uint)Zy87Integrity.Ranges[index].End);
        }
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0x0FFFFC), 0x7100);
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0x7100), 0x36344630);
        return Zy87Integrity.Correct(rom);
    }

    [Fact]
    public void AcceptsMatchingImagesWithOnlyChecksumValidCalibrationChanges()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Mazda2FlashPreflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var original = Synthetic();
            var modified = original.ToArray();
            modified[0x0C1000] ^= 0x10;
            modified[0x0D2000] ^= 0x01;
            modified = Zy87Integrity.Correct(modified);
            string originalPath = CreateVerifiedBackup(folder, original);
            string modifiedPath = Path.Combine(folder, "modified.rom");
            File.WriteAllBytes(modifiedPath, modified);

            var result = Mazda2FlashPreflight.Analyze(originalPath, modifiedPath);

            Assert.True(result.ReadyForDryRun);
            Assert.Equal(Mazda2FlashPreflight.SoftwareId, result.SoftwareId);
            Assert.Equal(2, result.ChangedCalibrationBytes);
            Assert.Equal(0, result.ChangedBootBytes);
            Assert.Equal(0, result.ChangedProgramBytes);
            Assert.Equal(0, result.ChangedOtherBytes);
            Assert.Equal(19, result.OriginalChecksums.Count);
            Assert.Equal(19, result.ModifiedChecksums.Count);
        }
        finally { Cleanup(folder); }
    }

    [Theory]
    [InlineData(0x000100)]
    [InlineData(0x008100)]
    [InlineData(0x0FFB00)]
    public void RejectsChangesOutsideCalibration(int address)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Mazda2FlashPreflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var original = Synthetic();
            var modified = original.ToArray();
            modified[address] ^= 0x01;
            modified = Zy87Integrity.Correct(modified);
            string a = CreateVerifiedBackup(folder, original), b = Path.Combine(folder, "b.rom");
            File.WriteAllBytes(b, modified);
            Assert.Throws<InvalidDataException>(() => Mazda2FlashPreflight.Analyze(a, b));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void RejectsUncorrectedModifiedChecksum()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Mazda2FlashPreflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var original = Synthetic();
            var modified = original.ToArray();
            modified[0x0C1000] ^= 0x10;
            string a = CreateVerifiedBackup(folder, original), b = Path.Combine(folder, "b.rom");
            File.WriteAllBytes(b, modified);
            Assert.Throws<InvalidDataException>(() => Mazda2FlashPreflight.Analyze(a, b));
        }
        finally { Directory.Delete(folder, true); }
    }
}
