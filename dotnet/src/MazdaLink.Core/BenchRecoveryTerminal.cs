using System.Text.Json;

namespace MazdaLink.Core;

public sealed class BenchRecoveryTerminal(Func<IReadTransport> connect, Func<IBenchMonitorChannel> monitor,
    TextReader input, TextWriter output, string dataRoot)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        Zyj2RecoveryPolicy.RequireBackup(backup);
        output.WriteLine("ZYJ2 SPARE ECU — BENCH MONITOR IDENTIFICATION ONLY\n" +
            "Use the detached ZYJ2 spare on your bench harness. Keep the good ECU disconnected.\n" +
            "This checks cold-start monitor entry and normal restart. It does not test interrupted-flash recovery.\n" +
            "No erase, ROM write, kernel upload, memory write, execute or ECU reset command is implemented.\n" +
            "The Mongoose must remain on USB while ECU battery power is cycled. Its CAN channel may fail when battery power is removed; the check will stop if that happens.\n" +
            "Type BENCH ZYJ2 to confirm the detached spare and bench setup:");
        if (input.ReadLine() != "BENCH ZYJ2") throw new InvalidDataException("Bench setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), "bench-checks", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        int sequence = 0;
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Bench evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { sequence, utc = DateTime.UtcNow, stage,
                calibration = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = RomStorage.Hash(backup.Data),
                flashOperationsImplemented = false, interruptedFlashRecoveryTested = false });
            journal.Write(line); journal.WriteByte((byte)'\n'); journal.Flush(flushToDisk: true);
            if (stage == "entry-window-armed")
                output.WriteLine("ENTRY WINDOW: cycle the spare's battery power now; leave it connected afterwards. Waiting 20 seconds...");
        }
        void ConfirmNormal()
        {
            var link = connect();
            try { Zyj2RecoveryPolicy.RequireLiveTarget(link); }
            finally
            {
                link.Dispose();
                if (link.CleanupWarning is not null) throw new BenchCleanupException(link.CleanupWarning + " Disconnect both USB and ECU battery power before reconnecting.");
            }
        }
        try
        {
            Record("started");
            ConfirmNormal();
            Record("normal-identity-and-engine-stopped-confirmed");
            output.WriteLine("The spare reports ZYJ2EA000ZYV5050.\n" +
                "After you type START, keep battery power connected while the adapter opens.\n" +
                "Wait for the ENTRY WINDOW message. During that 20-second window,\n" +
                "disconnect ECU battery power for at least five seconds, then reconnect it and leave it on.\n" +
                "Keep USB connected. Do not change ECUs, CAN wiring or adapters.\n" +
                "Type START when ready for that single power cycle:");
            if (input.ReadLine() != "START") throw new InvalidDataException("Entry window cancelled. No bench entry traffic was sent.");
            cancel.ThrowIfCancellationRequested();
            var channel = monitor();
            try
            {
                output.WriteLine("Opening bench channel. Keep ECU battery power connected until the ENTRY WINDOW message.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
            }
            finally
            {
                channel.Dispose();
                if (channel.CleanupWarning is not null) throw new BenchCleanupException(channel.CleanupWarning + " Disconnect both USB and ECU battery power before reconnecting.");
            }
            output.WriteLine("MONITOR IDENTIFIED. No flash operation was sent.\n" +
                "Now disconnect ECU battery power for at least five seconds and reconnect it.\n" +
                "Leave it powered on. Press Enter when that second power cycle is complete:");
            if (input.ReadLine() is null) throw new InvalidDataException("Normal restart was not checked. Monitor identification alone is not a recovery pass.");
            cancel.ThrowIfCancellationRequested();
            ConfirmNormal();
            Record("normal-restart-confirmed");
            Record("identification-check-complete-not-flash-recovery-qualified");
            output.WriteLine("BENCH CHECK COMPLETE — monitor identification and normal diagnostic restart passed.\n" +
                "This does not establish recovery after an interrupted erase/write. Flashing remains unavailable.\nEvidence: " + folder);
        }
        catch (Exception error)
        {
            string reason = error.GetType().Name + ": " + error.Message;
            try { Record("incomplete-or-failed-no-recovery-qualification: " + reason[..Math.Min(reason.Length, 256)]); } catch (IOException) { }
            output.WriteLine("Bench check incomplete. Stop entry traffic, disconnect/reconnect the spare's battery power, and leave it on before normal diagnostics.\nEvidence: " + folder);
            if (error is BenchCleanupException) output.WriteLine("DRIVER CLEANUP UNCERTAIN: disconnect both USB and ECU battery power. Restart MazdaLink after reconnecting. The application will stop.");
            throw;
        }
    }
}
