using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed class ReadWorkflow
{
    public string Capture(IReadTransport link, string root, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        TargetPolicy.Identify(link); // No unlock or memory request before exact identity match.
        byte[] rpm = link.Exchange([1, 0x0C]);
        if (rpm.Length != 4 || rpm[0] != 0x41 || rpm[1] != 0x0C || BinaryPrimitives.ReadUInt16BigEndian(rpm.AsSpan(2)) != 0)
            throw new TransportException("Engine RPM could not be confirmed as zero. Stop the engine before reading.");
        TargetPolicy.Identify(link); // Recheck immediately before session/security access.
        cancel.ThrowIfCancellationRequested();
        byte[] session = link.Exchange([0x10, 3]);
        if (session.Length != 6 || session[0] != 0x50 || session[1] != 3) throw new TransportException("Read session was not acknowledged.");
        byte[] seed = link.Exchange([0x27, 3]);
        if (seed.Length != 5 || seed[0] != 0x67 || seed[1] != 3) throw new TransportException("Invalid read-access seed.");
        cancel.ThrowIfCancellationRequested();
        if (!seed.AsSpan(2).SequenceEqual(new byte[3]))
        {
            byte[] reply = link.Exchange([0x27, 4, .. SecurityAccess.SeedKey(seed.AsSpan(2))]);
            if (!reply.AsSpan().SequenceEqual(new byte[] { 0x67, 4 })) throw new TransportException("Read access was not acknowledged. No retry was attempted.");
        }
        cancel.ThrowIfCancellationRequested();
        byte[] header = Read(link, Zy87Integrity.IdentityOffset, TargetPolicy.SoftwareId.Length);
        if (!header.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(TargetPolicy.SoftwareId)))
            throw new InvalidDataException("The memory software identity does not match the reported calibration. No full backup was started.");
        string folder = Path.Combine(Path.GetFullPath(root), "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        RomStorage.SaveNew(Path.Combine(folder, "journal.json"), JsonSerializer.SerializeToUtf8Bytes(new BackupJournal(TargetPolicy.SoftwareId, "", "", false)));
        var captures = new List<byte[]>();
        for (int pass = 1; pass <= 2; pass++)
        {
            byte[] data = new byte[TargetPolicy.RomSize];
            using (var file = new FileStream(Path.Combine(folder, $"pass{pass}.read"), FileMode.CreateNew, FileAccess.Write))
            {
                try
                {
                    for (int address = 0; address < data.Length; address += 512)
                    {
                        cancel.ThrowIfCancellationRequested();
                        byte[] block = Read(link, address, Math.Min(512, data.Length - address));
                        block.CopyTo(data, address); file.Write(block);
                        progress?.Invoke(pass, address + block.Length);
                    }
                }
                finally { file.Flush(flushToDisk: true); }
            }
            if (!File.ReadAllBytes(Path.Combine(folder, $"pass{pass}.read")).AsSpan().SequenceEqual(data)) throw new IOException("Read capture failed disk verification.");
            captures.Add(data);
        }
        cancel.ThrowIfCancellationRequested();
        if (!captures[0].AsSpan().SequenceEqual(captures[1])) throw new InvalidDataException("The two full reads differ. Captures were retained; no verified ROM was saved.");
        Zy87Integrity.RequireLayout(captures[0]);
        string original = Path.Combine(folder, "original.rom");
        RomStorage.SaveNew(original, captures[0]); // Raw backups preserve checksum bytes exactly.
        var manifest = new BackupManifest(1, TargetPolicy.SoftwareId, RomStorage.Hash(captures[0]), TargetPolicy.RomSize, "hardware");
        RomStorage.SaveNew(Path.Combine(folder, "backup.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
        // The completion journal is committed last; interrupted operations cannot load as verified backups.
        string journalPath = Path.Combine(folder, "journal.json");
        File.Move(journalPath, Path.Combine(folder, "read-started.json"), overwrite: false);
        RomStorage.SaveNew(journalPath, JsonSerializer.SerializeToUtf8Bytes(new BackupJournal(TargetPolicy.SoftwareId, manifest.Sha256, manifest.Sha256, true)));
        RomStorage.LoadBackup(original);
        foreach (string name in new[] { "original.rom", "pass1.read", "pass2.read", "backup.json", "journal.json", "read-started.json" })
            File.SetAttributes(Path.Combine(folder, name), File.GetAttributes(Path.Combine(folder, name)) | FileAttributes.ReadOnly);
        return original;
    }
    private static byte[] Read(IReadTransport link, int address, int count)
    {
        byte[] reply = link.Exchange(DiagnosticProtocol.ReadRequest(address, count));
        if (reply.Length != count + 1 || reply[0] != 0x63) throw new TransportException($"Invalid read at 0x{address:X6}. No retry was attempted.");
        return reply[1..];
    }
}
