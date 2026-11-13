using System.Buffers.Binary;
using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class Zy87IntegrityTests
{
    // Synthetic image with the ZY87EC000 layout: random content, the real table addresses, ID and boot marker.
    private static byte[] Synthetic(int seed = 87)
    {
        var rom = new byte[RomInspector.RomSize];
        new Random(seed).NextBytes(rom);
        Encoding.ASCII.GetBytes("ZY87EC000ZYV5050").CopyTo(rom, Zy87Integrity.IdentityOffset);
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

    // Independent reference: the ECU loop reads words while the pointer is below the inclusive end.
    private static uint Reference(byte[] rom, int start, int end)
    {
        uint sum = 0;
        for (int pointer = start; pointer < end; pointer += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(rom.AsSpan(pointer)));
        return sum;
    }

    [Fact]
    public void CorrectedImagePassesEveryRangeAndMatchesTheReferenceSum()
    {
        var rom = Synthetic();
        var checks = Zy87Integrity.Check(rom);
        Assert.Equal(19, checks.Count);
        Assert.All(checks, range => Assert.True(range.Valid));
        foreach (var range in checks)
            Assert.Equal(Zy87Integrity.Target, unchecked(Reference(rom, range.Start, range.End) + range.Stored));
    }

    [Fact]
    public void CalibrationEditFailsOnlyRangeElevenAndCorrectionChangesOnlyItsWord()
    {
        var rom = Synthetic();
        var edited = rom.ToArray();
        edited[0x0DB970] ^= 0x40;
        edited[0x0D53A0] ^= 0x01;
        Assert.Equal(new[] { 11 }, Zy87Integrity.Check(edited).Where(r => !r.Valid).Select(r => r.Index));
        var fixedRom = Zy87Integrity.Correct(edited);
        Assert.All(Zy87Integrity.Check(fixedRom), range => Assert.True(range.Valid));
        var changed = Enumerable.Range(0, rom.Length).Where(i => fixedRom[i] != edited[i]).ToArray();
        Assert.All(changed, offset => Assert.InRange(offset, 0x0FF6DC, 0x0FF6DF));
        Assert.Equal(0x0FF6DC, Zy87Integrity.CorrectionAddress(11));
    }

    [Fact]
    public void CorrectingAValidImageChangesNothing()
    {
        var rom = Synthetic(5);
        Assert.Equal(rom, Zy87Integrity.Correct(rom));
    }

    [Fact]
    public void CompareCountsEditsByRegionAndIgnoresCorrectionWords()
    {
        var rom = Synthetic();
        var edited = rom.ToArray();
        edited[0x0C1000] ^= 1;
        edited[0x0C2000] ^= 1;
        edited[0x050000] ^= 1;
        var changes = Zy87Integrity.Compare(rom, Zy87Integrity.Correct(edited));
        Assert.Equal(new Zy87RegionChanges(0, 1, 2, 0), changes);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("table")]
    [InlineData("extra-entry")]
    [InlineData("boot-marker")]
    [InlineData("size")]
    public void RefusesAnythingThatIsNotTheKnownLayout(string damage)
    {
        var rom = Synthetic();
        switch (damage)
        {
            case "id": rom[Zy87Integrity.IdentityOffset + 3] = (byte)'9'; break;
            case "table": BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(Zy87Integrity.TableAddress + 11 * 12 + 4), 0x0F8FFF); break;
            case "extra-entry": BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(Zy87Integrity.TableAddress + 19 * 12), 0x0FB000); break;
            case "boot-marker": BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0x0FFFFC), 0xFFFFFFFF); break;
        }
        var image = damage == "size" ? rom[..0x80000] : rom;
        Assert.Throws<InvalidDataException>(() => Zy87Integrity.Correct(image));
        Assert.Throws<InvalidDataException>(() => Zy87Integrity.Check(image));
    }
}
