using System.Buffers.Binary;
using System.Text;

namespace SpeedLink.Core;

public sealed record Zy87RangeCheck(int Index, int Start, int End, uint Stored, uint Total)
{
    public bool Valid => Total == Zy87Integrity.Target;
}

public sealed record Zy87RegionChanges(int Boot, int Program, int Calibration, int Other);

/// <summary>
/// Checksum policy for Mazda 2 (DE) ZY-VE calibration ZY87EC000, established from the ECU's own
/// verification routine (0x01306E): a table at 0x0FF650 lists 19 ranges as (start, inclusive end,
/// correction). For each range, the 32-bit big-endian word sum of start..end plus the correction must
/// equal 0x5AA5A55A. The table, the boot block (0x000000-0x0070FF) and the boot validity marker are
/// outside every range. Only the 19 correction words are ever changed.
/// </summary>
public static class Zy87Integrity
{
    public const string Calibration = "ZY87EC000";
    public const int IdentityOffset = 0x0C0091;
    public const int TableAddress = 0x0FF650;
    public const uint Target = 0x5AA5A55A;
    public const int BootEnd = 0x007100;
    public const int CalibrationStart = 0x0C0000, CalibrationEnd = 0x0F8000;
    private const int EntryBytes = 12;
    private const int BootPointer = 0x0FFFFC;

    /// <summary>The exact range layout this policy supports; any other layout is refused.</summary>
    public static readonly (int Start, int End)[] Ranges =
    [
        (0x007100, 0x0BAFFF), (0x0BB000, 0x0BB7FF), (0x0BB800, 0x0BBFFF), (0x0BC000, 0x0BC7FF), (0x0BC800, 0x0BCFFF),
        (0x0BD000, 0x0BD7FF), (0x0BD800, 0x0BDFFF), (0x0BE000, 0x0BE7FF), (0x0BE800, 0x0BEFFF), (0x0BF000, 0x0BF7FF),
        (0x0BF800, 0x0BFFFF), (0x0C0000, 0x0F7FFF), (0x0F8000, 0x0F87FF), (0x0F8800, 0x0F8FFF), (0x0F9000, 0x0F97FF),
        (0x0F9800, 0x0F9FFF), (0x0FA000, 0x0FA7FF), (0x0FA800, 0x0FAFFF), (0x0FF800, 0x0FF9BF)
    ];

    public static int CorrectionAddress(int index) => TableAddress + index * EntryBytes + 8;

    /// <summary>Throws unless the image is a 1 MiB ZY87EC000 ROM with exactly the supported table and boot markers.</summary>
    public static void RequireLayout(ReadOnlySpan<byte> rom)
    {
        if (rom.Length != RomInspector.RomSize)
            throw new InvalidDataException($"Expected a {RomInspector.RomSize:N0}-byte ROM; this file is {rom.Length:N0} bytes.");
        if (!rom.Slice(IdentityOffset, Calibration.Length).SequenceEqual(Encoding.ASCII.GetBytes(Calibration)))
            throw new InvalidDataException($"This is not a {Calibration} ROM (calibration ID not found at 0x{IdentityOffset:X6}).");
        for (int index = 0; index < Ranges.Length; index++)
        {
            int entry = TableAddress + index * EntryBytes;
            if (BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(entry, 4)) != (uint)Ranges[index].Start ||
                BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(entry + 4, 4)) != (uint)Ranges[index].End)
                throw new InvalidDataException($"Checksum table entry {index} at 0x{entry:X6} does not match the known {Calibration} layout.");
        }
        if (BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(TableAddress + Ranges.Length * EntryBytes, 4)) != 0xFFFFFFFF)
            throw new InvalidDataException("The checksum table has more entries than the known layout.");
        if (BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(BootPointer, 4)) != BootEnd ||
            BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(BootEnd, 4)) == 0xFFFFFFFF)
            throw new InvalidDataException("The boot validity marker is missing or changed; this ROM would not start.");
    }

    public static IReadOnlyList<Zy87RangeCheck> Check(ReadOnlySpan<byte> rom)
    {
        RequireLayout(rom);
        var results = new Zy87RangeCheck[Ranges.Length];
        for (int index = 0; index < Ranges.Length; index++)
        {
            uint stored = BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(CorrectionAddress(index), 4));
            results[index] = new(index, Ranges[index].Start, Ranges[index].End, stored, unchecked(Sum(rom, index) + stored));
        }
        return results;
    }

    /// <summary>Returns a copy with every correction word recalculated, verified before it is returned.</summary>
    public static byte[] Correct(ReadOnlySpan<byte> rom)
    {
        RequireLayout(rom);
        byte[] output = rom.ToArray();
        for (int index = 0; index < Ranges.Length; index++)
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(CorrectionAddress(index), 4), unchecked(Target - Sum(output, index)));
        if (Check(output).Any(range => !range.Valid))
            throw new InvalidDataException("Checksum correction failed verification.");
        for (int offset = 0; offset < output.Length; offset++)
            if (output[offset] != rom[offset] && !IsCorrectionByte(offset))
                throw new InvalidDataException($"Checksum correction changed an unexpected byte at 0x{offset:X6}.");
        return output;
    }

    /// <summary>Counts changed bytes by region, ignoring the 19 correction words.</summary>
    public static Zy87RegionChanges Compare(ReadOnlySpan<byte> original, ReadOnlySpan<byte> edited)
    {
        RequireLayout(original);
        RequireLayout(edited);
        int boot = 0, program = 0, calibration = 0, other = 0;
        for (int offset = 0; offset < original.Length; offset++)
        {
            if (original[offset] == edited[offset] || IsCorrectionByte(offset))
                continue;
            if (offset < BootEnd) boot++;
            else if (offset < CalibrationStart) program++;
            else if (offset < CalibrationEnd) calibration++;
            else other++;
        }
        return new(boot, program, calibration, other);
    }

    private static bool IsCorrectionByte(int offset)
    {
        int relative = offset - TableAddress;
        return relative >= 0 && relative < Ranges.Length * EntryBytes && relative % EntryBytes >= 8;
    }

    private static uint Sum(ReadOnlySpan<byte> rom, int index)
    {
        var (start, end) = Ranges[index];
        uint sum = 0;
        for (int offset = start; offset < end; offset += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(rom.Slice(offset, 4)));
        return sum;
    }
}
