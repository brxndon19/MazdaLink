using System.Globalization;
using System.Text.Json.Nodes;

namespace SpeedLink.Core;

/// <summary>Compares a later verified backup with the image bound to a saved flash journal.</summary>
internal sealed record FlashVerification(string Calibration, string ImageHash, DateTimeOffset ResetAt,
    string VehicleHash, string Mode)
{
    internal static FlashVerification Load(string journalPath, string mode)
    {
        var store = new BackupStore();
        var folder = Path.GetDirectoryName(Path.GetFullPath(journalPath))!;
        var imagePath = Path.Combine(folder, "intended-image.bin");
        if (new FileInfo(imagePath).Length != RomInspector.RomSize)
            throw new InvalidDataException("The intended image is missing or is not a complete 1 MiB ROM.");
        var image = store.FileSystem.Read(imagePath);
        if (image.Length != RomInspector.RomSize)
            throw new InvalidDataException("The intended image changed while reading.");
        string hash = RomInspector.Hash(image);
        var profile = new RomInspector().ProfileFor(image);
        if (profile is null || !BenchFlashWorkflow.IsNativeWriterVerified(profile.Calibration))
            throw new InvalidDataException("The saved image does not match a supported Gen 1 calibration.");

        string? vehicleHash = null;
        DateTimeOffset? reset = null;
        int issued = 0, acknowledged = 0;
        var request = new byte[BenchFlashPlan.Chunk + 1]; request[0] = 0x36;
        var journal = store.InspectJournal(journalPath, rows =>
        {
            foreach (var row in rows)
            {
                var detail = row["details"]!.AsObject();
                switch (row["event"]!.GetValue<string>())
                {
                    case "recovery_binding":
                        if (vehicleHash is not null || detail["calibration"]?.GetValue<string>() != profile.Identity ||
                            detail["bootSha256"]?.GetValue<string>() != RomInspector.Hash(image.AsSpan(0, BenchFlashPlan.Start)))
                            throw new InvalidDataException("The intended image differs from the recorded boot bytes or calibration.");
                        vehicleHash = detail["vehicleIdentitySha256"]?.GetValue<string>() ?? throw new InvalidDataException("Missing ECU identity.");
                        break;
                    case "request_issued" when detail["service"]?.GetValue<int>() == 0x36:
                        if (issued != acknowledged || issued >= BenchFlashPlan.Length || vehicleHash is null)
                            throw new InvalidDataException("The recorded transfer sequence is incomplete.");
                        image.AsSpan(BenchFlashPlan.Start + issued, BenchFlashPlan.Chunk).CopyTo(request.AsSpan(1));
                        if (detail["sha256"]?.GetValue<string>() != RomInspector.Hash(request))
                            throw new InvalidDataException("The intended image differs from the recorded transfer.");
                        issued += BenchFlashPlan.Chunk;
                        break;
                    case "block_acknowledged":
                        if (issued != acknowledged + BenchFlashPlan.Chunk || detail["total"]?.GetValue<int>() != issued)
                            throw new InvalidDataException("The recorded transfer acknowledgement is out of sequence.");
                        acknowledged = issued;
                        break;
                    case "response_received" when detail["service"]?.GetValue<int>() == 0x11 && detail["responseHex"]?.GetValue<string>() == "51":
                        if (acknowledged != BenchFlashPlan.Length || reset is not null)
                            throw new InvalidDataException("The reset did not follow a complete transfer.");
                        reset = Timestamp(row);
                        break;
                }
            }
        });
        if (!journal.Valid || journal.Mode != mode || journal.Operation is not ("local_bench_flash" or "local_bench_recovery") ||
            reset is not DateTimeOffset resetAt || vehicleHash is null)
            throw new InvalidDataException(journal.Error ?? "The last write has no acknowledged reset. Check the run status and follow the recovery guide if needed.");
        return new(profile.Identity, hash, resetAt, vehicleHash, mode);
    }

    internal bool CompareBackup(string path)
    {
        var store = new BackupStore();
        var backup = store.Load(path, Mode);
        var saved = backup.Manifest["identity"]?.AsObject() ?? throw new InvalidDataException("The backup has no ECU identity.");
        var identity = BenchFlashPlan.ReadIdentity(saved, Mode);
        if (identity.Calibration != Calibration && identity.Calibration != Calibration[..9] ||
            string.IsNullOrWhiteSpace(identity.VehicleIdentifier) || identity.Vin is not null && identity.ChassisId is not null ||
            BenchFlashPlan.HashVehicleIdentity(identity) != VehicleHash)
            throw new InvalidDataException("Choose a verified backup from the same ECU and calibration as the write.");

        var journalPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "journal.jsonl");
        var hash = RomInspector.Hash(backup.Data);
        var journal = store.InspectJournal(journalPath, rows =>
        {
            if (Timestamp(rows[0]) <= ResetAt)
                throw new InvalidDataException("Read a new verified backup after the selected write's reset.");
            var observed = rows.SingleOrDefault(row => row["event"]?.GetValue<string>() == "identity")?["details"]?["identity"] as JsonObject;
            if (observed is null || !identity.MatchesVehicle(BenchFlashPlan.ReadIdentity(observed, Mode)))
                throw new InvalidDataException("The backup identity differs from its read journal.");
        });
        if (!journal.Valid || !journal.Complete || journal.Mode != Mode || journal.Operation != "read_backup" ||
            journal.LastDetails?["sha256"]?.GetValue<string>() != hash)
            throw new InvalidDataException(journal.Error ?? "The verified backup journal is no longer intact.");
        return hash == ImageHash;
    }

    private static DateTimeOffset Timestamp(JsonObject row) =>
        DateTimeOffset.TryParse(row["time"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : throw new InvalidDataException("The journal has an invalid timestamp.");
}
