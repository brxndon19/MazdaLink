using System.Security.Cryptography;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed record BackupManifest(int Schema, string SoftwareId, string Sha256, int Size, string Mode);
public sealed record BackupJournal(string SoftwareId, string Pass1Sha256, string Pass2Sha256, bool Complete);
public sealed record VerifiedRawBackup(byte[] Data, string ReportedCalibration);
public static class RomStorage
{
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static void SaveNew(string path, ReadOnlySpan<byte> data, Action<byte[]>? verify = null)
    {
        byte[] expected = data.ToArray();
        verify?.Invoke(expected);
        string output = Path.GetFullPath(path), partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(expected);
                file.Flush(flushToDisk: true);
            }
            byte[] durable = File.ReadAllBytes(partial);
            if (!durable.AsSpan().SequenceEqual(expected)) throw new IOException("The disk copy does not match the prepared bytes.");
            verify?.Invoke(durable);
            File.Move(partial, output, overwrite: false);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public static byte[] LoadBackup(string path) => LoadVerifiedBackup(path).Data;
    public static byte[] LoadEditorBackup(string path)
    {
        var backup = LoadVerifiedBackup(path);
        if (backup.ReportedCalibration != TargetPolicy.SoftwareId)
            throw new InvalidDataException($"Checksum preparation/validation is not established for {backup.ReportedCalibration}. Raw read/export is available; the known checksum policy is only for {TargetPolicy.SoftwareId}.");
        TargetPolicy.RequireSoftware(backup.Data);
        return backup.Data;
    }
    public static VerifiedRawBackup LoadVerifiedBackup(string path)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (Path.GetFileName(path) != "original.rom") throw new InvalidDataException("Choose original.rom inside a complete MazdaLink backup folder.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(root, "backup.json")))
            ?? throw new InvalidDataException("Backup manifest is missing.");
        if (manifest.Schema != 1 || manifest.Size != TargetPolicy.RomSize || manifest.Mode != "hardware")
            throw new InvalidDataException("This is not a supported MazdaLink hardware backup.");
        var rom = File.ReadAllBytes(path);
        TargetPolicy.RequireIdentity(manifest.SoftwareId);
        if (rom.Length != TargetPolicy.RomSize) throw new InvalidDataException("The raw backup must contain the complete 1 MiB capture.");
        if (Hash(rom) != manifest.Sha256) throw new InvalidDataException("The original backup was changed.");
        var journal = JsonSerializer.Deserialize<BackupJournal>(File.ReadAllText(Path.Combine(root, "journal.json")));
        if (journal is null || !journal.Complete || journal.SoftwareId != manifest.SoftwareId || journal.Pass1Sha256 != manifest.Sha256 || journal.Pass2Sha256 != manifest.Sha256)
            throw new InvalidDataException("The backup has no matching completed read journal.");
        foreach (string file in new[] { "original.rom", "pass1.read", "pass2.read" })
        {
            byte[] capture = File.ReadAllBytes(Path.Combine(root, file));
            if (capture.Length != manifest.Size || Hash(capture) != manifest.Sha256) throw new InvalidDataException("The backup or a retained read capture was changed.");
        }
        return new(rom, manifest.SoftwareId);
    }
    public static void Export(string backup, string output) => SaveNew(output, LoadBackup(backup));
}

/// <summary>Only supported path for preparing modified ROM copies; never changes the original or input.</summary>
public static class RomEditor
{
    public static Zy87RegionChanges Validate(string originalBackup, string modified, bool requireChecksums = true)
    {
        return ValidateBytes(RomStorage.LoadEditorBackup(originalBackup), File.ReadAllBytes(modified), requireChecksums);
    }
    private static Zy87RegionChanges ValidateBytes(byte[] original, byte[] edited, bool requireChecksums)
    {
        Zy87Integrity.RequireLayout(original); Zy87Integrity.RequireLayout(edited);
        if (Zy87Integrity.Check(original).Any(x => !x.Valid)) throw new InvalidDataException("The original backup fails checksum verification.");
        var changes = Zy87Integrity.Compare(original, edited);
        if (changes.Boot != 0 || changes.Program != 0 || changes.Other != 0)
            throw new InvalidDataException("Only changes inside the established calibration region are accepted. Boot, program, identity and other bytes must remain unchanged.");
        if (requireChecksums && Zy87Integrity.Check(edited).Any(x => !x.Valid)) throw new InvalidDataException("The modified ROM has invalid checksums. Use Prepare modified ROM first.");
        return changes;
    }
    public static void SaveNew(string originalBackup, string modified, string output)
    {
        byte[] original = RomStorage.LoadEditorBackup(originalBackup), edited = File.ReadAllBytes(modified);
        ValidateBytes(original, edited, requireChecksums: false);
        byte[] prepared = Zy87Integrity.Correct(edited);
        RomStorage.SaveNew(output, prepared, bytes =>
        {
            if (Zy87Integrity.Check(bytes).Any(x => !x.Valid)) throw new InvalidDataException("Prepared ROM failed checksum verification.");
        });
    }
}
