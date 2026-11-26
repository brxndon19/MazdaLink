namespace MazdaLink.Core;

/// <summary>Offline difference inventory only; cannot be converted into a native flash capability.</summary>
public static class ModifiedRomPlan
{
    public static object Create(string originalBackup, string preparedRom)
    {
        byte[] original = RomStorage.LoadEditorBackup(originalBackup), prepared = File.ReadAllBytes(preparedRom);
        var changes = RomEditor.ValidateBytes(original, prepared, requireChecksums:true);
        if (RomIntegrity.Software(original) != Zyj2RecoveryPolicy.SoftwareId)
            throw new InvalidDataException("The offline modified-block report is limited to ZYJ2EA000ZYV5050.");
        int Count(int start, int end) => Enumerable.Range(start, end-start).Count(i => original[i] != prepared[i]);
        return new { schema=1, hardwareAccessed=false, eraseOrProgrammingEnabled=false,
            targetProfile=Zyj2RecoveryPolicy.SoftwareId, originalSha256=RomStorage.Hash(original), preparedSha256=RomStorage.Hash(prepared),
            calibrationBytesChanged=changes.Calibration,
            blocks=new[] { new { block=14, start=0xC0000, length=0x20000, changedBytes=Count(0xC0000,0xE0000) },
                new { block=15, start=0xE0000, length=0x20000, changedBytes=Count(0xE0000,0x100000) } },
            correctionAddress=Zyj2Integrity.CorrectionAddress(11), checksumRangesVerified=19,
            modifiedFlashingEnabled=false, eb15WritingImplemented=true, eb15WritingScope="original-only-detached-spare-qualification", eb15ModifiedWritingImplemented=false,
            limitation="Offline checksum/difference report only. The existing original-only EB14 writer rejects this modified image. EB15 write/recovery and active-operation power-loss recovery remain unqualified." };
    }
}
