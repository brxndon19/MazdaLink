using System.Text;

namespace SpeedLink.Core;

public sealed record Mazda2FlashPreflightResult(
    string OriginalPath,
    string ModifiedPath,
    string OriginalSha256,
    string ModifiedSha256,
    string SoftwareId,
    int ChangedCalibrationBytes,
    int ChangedProgramBytes,
    int ChangedBootBytes,
    int ChangedOtherBytes,
    IReadOnlyList<Zy87RangeCheck> OriginalChecksums,
    IReadOnlyList<Zy87RangeCheck> ModifiedChecksums)
{
    public bool ReadyForDryRun => ChangedBootBytes == 0 && ChangedProgramBytes == 0 && ChangedOtherBytes == 0 &&
        OriginalChecksums.All(x => x.Valid) && ModifiedChecksums.All(x => x.Valid);
}

/// <summary>
/// Non-destructive preflight for the Mazda 2 DE ZY-VE / SH7058 ZY87EC000 image.
/// This class deliberately has no transport or ECU-write dependency.
/// </summary>
public static class Mazda2FlashPreflight
{
    public const string SoftwareId = "ZY87EC000ZYV5050";

    public static Mazda2FlashPreflightResult Analyze(string originalPath, string modifiedPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath) || string.IsNullOrWhiteSpace(modifiedPath))
            throw new ArgumentException("Select both the original ECU backup and the modified ROM.");

        var original = File.ReadAllBytes(originalPath);
        var modified = File.ReadAllBytes(modifiedPath);
        Zy87Integrity.RequireLayout(original);
        Zy87Integrity.RequireLayout(modified);

        var originalId = ReadSoftwareId(original);
        var modifiedId = ReadSoftwareId(modified);
        if (!string.Equals(originalId, SoftwareId, StringComparison.Ordinal) ||
            !string.Equals(modifiedId, SoftwareId, StringComparison.Ordinal))
            throw new InvalidDataException($"Both images must identify as {SoftwareId}.");

        if (!string.Equals(originalId, modifiedId, StringComparison.Ordinal))
            throw new InvalidDataException("The original backup and modified ROM are from different software versions.");

        var originalChecks = Zy87Integrity.Check(original);
        var modifiedChecks = Zy87Integrity.Check(modified);
        if (originalChecks.Any(x => !x.Valid))
            throw new InvalidDataException("The original backup does not pass all 19 ZY87 checksum ranges.");
        if (modifiedChecks.Any(x => !x.Valid))
            throw new InvalidDataException("The modified ROM does not pass all 19 ZY87 checksum ranges. Correct and re-check its checksums before proceeding.");

        var changes = Zy87Integrity.Compare(original, modified);
        if (changes.Boot != 0)
            throw new InvalidDataException($"The ROM changes {changes.Boot} boot-block byte(s). Boot code must remain identical to the original backup.");
        if (changes.Program != 0)
            throw new InvalidDataException($"The ROM changes {changes.Program} engine-program byte(s). This workflow only accepts calibration changes.");
        if (changes.Other != 0)
            throw new InvalidDataException($"The ROM changes {changes.Other} byte(s) outside the known program and calibration regions.");

        return new(originalPath, modifiedPath, RomInspector.Hash(original), RomInspector.Hash(modified),
            originalId, changes.Calibration, changes.Program, changes.Boot, changes.Other, originalChecks, modifiedChecks);
    }

    private static string ReadSoftwareId(ReadOnlySpan<byte> rom)
        => Encoding.ASCII.GetString(rom.Slice(Zy87Integrity.IdentityOffset, SoftwareId.Length));
}
