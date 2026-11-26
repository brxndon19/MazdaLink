using System.Text;

namespace MazdaLink.Core;

/// <summary>Explicit checksum profile selection. Never used to restrict raw backup reads.</summary>
public static class RomIntegrity
{
    public const string Supported = "ZY87EC000ZYV5050, ZYJ2EA000ZYV5050";
    public static string Software(ReadOnlySpan<byte> rom)
    {
        if (rom.Length != TargetPolicy.RomSize) throw new InvalidDataException("A complete 1 MiB ROM is required.");
        string id = Encoding.ASCII.GetString(rom.Slice(Zy87Integrity.IdentityOffset, TargetPolicy.SoftwareId.Length));
        if (id != TargetPolicy.SoftwareId && id != Zyj2RecoveryPolicy.SoftwareId)
            throw new InvalidDataException("Checksum policy is not established for this exact embedded software ID.");
        return id;
    }
    public static void RequireLayout(ReadOnlySpan<byte> rom)
    { if (Software(rom) == TargetPolicy.SoftwareId) Zy87Integrity.RequireLayout(rom); else Zyj2Integrity.RequireLayout(rom); }
    public static IReadOnlyList<Zy87RangeCheck> Check(ReadOnlySpan<byte> rom) =>
        Software(rom) == TargetPolicy.SoftwareId ? Zy87Integrity.Check(rom) : Zyj2Integrity.Check(rom);
    public static byte[] Correct(ReadOnlySpan<byte> rom) =>
        Software(rom) == TargetPolicy.SoftwareId ? Zy87Integrity.Correct(rom) : Zyj2Integrity.Correct(rom);
    public static Zy87RegionChanges Compare(ReadOnlySpan<byte> original, ReadOnlySpan<byte> edited)
    {
        if (Software(original) != Software(edited)) throw new InvalidDataException("Checksum profiles differ.");
        return Software(original) == TargetPolicy.SoftwareId ? Zy87Integrity.Compare(original, edited) : Zyj2Integrity.Compare(original, edited);
    }
}
