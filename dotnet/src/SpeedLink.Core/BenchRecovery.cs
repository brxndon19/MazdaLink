using System.Text.Json.Nodes;

namespace SpeedLink.Core;

/// <summary>Boot-only stock restore is tied to a recorded destructive operation and its original verified backup.</summary>
public sealed class BenchRecoveryPlan
{
    public BenchFlashPlan Stock { get; }
    public string PriorJournalSha256 { get; }
    private BenchRecoveryPlan(BenchFlashPlan stock, string priorJournalSha256)
    {
        Stock = stock;
        PriorJournalSha256 = priorJournalSha256;
    }

    public static BenchRecoveryPlan Load(string journalPath, string backupPath, string requiredMode = "hardware")
        => LoadCore(journalPath, BenchFlashPlan.Load(backupPath, backupPath, requiredMode), requiredMode);

    internal static BenchRecoveryPlan LoadSimulation(string journalPath, string backupPath, RomInspector inspector, TuningCodeGuard codeGuard)
        => LoadCore(journalPath, BenchFlashPlan.LoadSimulation(backupPath, backupPath, inspector, codeGuard), "simulator");

    private static BenchRecoveryPlan LoadCore(string journalPath, BenchFlashPlan stock, string requiredMode)
    {
        if (Path.GetFileName(journalPath) != "journal.jsonl")
            throw new InvalidDataException("Select the interrupted flash journal.");
        var store = new BackupStore();
        string journalHash = RomInspector.Hash(store.FileSystem.Read(journalPath));
        JsonObject? binding = null;
        var eraseIntent = false;
        var resetAcknowledged = false;
        var previousBinding = false;
        var journal = store.InspectJournal(journalPath, rows =>
        {
            foreach (var row in rows)
            {
                var details = row["details"]!.AsObject();
                switch (row["event"]!.GetValue<string>())
                {
                    case "recovery_binding":
                        if (previousBinding || eraseIntent) throw new InvalidDataException("Unexpected recovery binding.");
                        binding = details;
                        previousBinding = true;
                        break;
                    case "destructive_boundary":
                        if (eraseIntent || binding is null || details["start"]?.GetValue<int>() != BenchFlashPlan.Start ||
                            details["bytes"]?.GetValue<int>() != BenchFlashPlan.Length)
                            throw new InvalidDataException("Invalid recorded erase intent.");
                        eraseIntent = true;
                        break;
                    case "response_received":
                        if (details["service"]?.GetValue<int>() == 0x11 && details["responseHex"]?.GetValue<string>() == "51")
                            resetAcknowledged = true;
                        break;
                }
            }
        });
        if (!journal.Valid || journal.Complete || journal.Mode != requiredMode ||
            journal.Operation is not ("local_bench_flash" or "local_bench_recovery") ||
            !eraseIntent || resetAcknowledged || binding is null ||
            binding["calibration"]?.GetValue<string>() != stock.FullCalibration ||
            binding["backupSha256"]?.GetValue<string>() != stock.BackupSha256 ||
            binding["bootSha256"]?.GetValue<string>() != stock.BootSha256 ||
            binding["vehicleIdentitySha256"]?.GetValue<string>() != stock.VehicleIdentityHash ||
            RomInspector.Hash(store.FileSystem.Read(journalPath)) != journalHash)
            throw new InvalidDataException("Recovery requires an intact interrupted erase/write journal tied to this exact backup.");
        return new(stock, journalHash);
    }
}
