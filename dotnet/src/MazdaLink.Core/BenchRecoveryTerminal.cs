using System.Text.Json;
using System.Reflection;

namespace MazdaLink.Core;

public sealed class BenchRecoveryTerminal(Func<IReadTransport> connect, Func<IBenchMonitorChannel> monitor,
    TextReader input, TextWriter output, string dataRoot, bool runRamSelfTest = false,
    Action<int, CancellationToken>? coldWait = null)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        Zyj2RecoveryPolicy.RequireBackup(backup);
        output.WriteLine((runRamSelfTest ? "ZYJ2 SPARE ECU — FIXED VOLATILE RAM SELF-TEST\n" : "ZYJ2 SPARE ECU — BENCH MONITOR IDENTIFICATION ONLY\n") +
            "Use the detached ZYJ2 spare on your bench harness. Keep the good ECU disconnected.\n" +
            (runRamSelfTest ? "This uploads only MazdaLink's fixed 64-byte RAM test, reads back every byte, queries the resident hardware class, reads flash-controller status and returns to the monitor.\n" +
                "The resident class query temporarily enables H-UDI status access and restores SYSCR2, as in native RAM copying. No flash-control or FWE-setting write, flash kernel, erase or ROM programming is implemented.\n" :
                "This checks cold-start monitor entry and normal restart. No upload, memory write, execute, erase, ROM programming or ECU reset is sent by this identification-only workflow.\n") +
            "This does not test interrupted-flash recovery. RAM execution and ECU flashing remain unqualified.\n" +
            "Keep Mongoose USB and its OBD 12 V supply powered. Switch only the ECU positive supply feeds; keep grounds and CAN connected.\n" +
            $"Type {(runRamSelfTest ? "RAM ZYJ2" : "BENCH ZYJ2")} to confirm the detached spare and bench setup:");
        if (input.ReadLine() != (runRamSelfTest ? "RAM ZYJ2" : "BENCH ZYJ2")) throw new InvalidDataException("Bench setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), runRamSelfTest ? "ram-self-tests" : "bench-checks", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        IBenchMonitorChannel? activeChannel = null;
        RamSelfTestResult? ramResult = null;
        string? version = typeof(BenchRecoveryTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        int sequence = 0;
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Bench evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { schema = 5, version, sequence, utc = DateTime.UtcNow, stage,
                calibration = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = RomStorage.Hash(backup.Data),
                flashOperationsImplemented = false, interruptedFlashRecoveryTested = false,
                fixedRamSelfTest = runRamSelfTest, ramResult,
                ramTransport = (activeChannel as IFixedRamSelfTestChannel)?.SelfTestDiagnostics,
                diagnostics = activeChannel?.Diagnostics }, jsonOptions);
            journal.Write(line); journal.WriteByte((byte)'\n'); journal.Flush(flushToDisk: true);
            if (stage == "entry-window-armed")
                output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
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
            output.WriteLine("The spare reports ZYJ2EA000ZYV5050 and stopped-engine RPM.");
            BenchColdStart.Prepare(input, output, Record, cancel, coldWait);
            var channel = monitor();
            activeChannel = channel;
            try
            {
                if (runRamSelfTest && channel is not IFixedRamSelfTestChannel)
                    throw new TransportException("The separate fixed RAM self-test backend is unavailable.");
                output.WriteLine("Opening bench channel. Keep ECU positive feeds disconnected until ENTRY WINDOW.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
                if (runRamSelfTest)
                {
                    ramResult = new Zyj2RamSelfTest().Run((IFixedRamSelfTestChannel)channel, Record, cancel);
                    Record("fixed-ram-self-test-result-recorded-no-flash");
                    output.WriteLine($"RAM SELF-TEST RETURNED TO MONITOR. {ramResult.VerifiedBytes} bytes verified before execution.\n" +
                        $"Resident hardware class: {ramResult.HardwareClass} — {ramResult.HardwareClassName}. Erase geometry must match the confirmed device.\n" +
                        $"Read-only status: FWE={(ramResult.FlashWriteEnableObserved ? 1 : 0)}, FLER={(ramResult.FlashErrorObserved ? 1 : 0)}, MAT=0x{ramResult.MatSelection:X2}. These observations do not authorize flashing.");
                }
            }
            finally
            {
                channel.Dispose();
                if (channel.CleanupWarning is not null) throw new BenchCleanupException(channel.CleanupWarning + " Disconnect both USB and ECU battery power before reconnecting.");
            }
            output.WriteLine("MONITOR IDENTIFIED. No flash operation was sent.");
            BenchColdStart.PrepareNormalRestart(input, output, Record, cancel, coldWait);
            ConfirmNormal();
            Record("normal-restart-confirmed");
            if (runRamSelfTest) Record("fixed-ram-self-test-and-normal-restart-complete-not-flash-qualified");
            Record("identification-check-complete-not-flash-recovery-qualified");
            output.WriteLine((runRamSelfTest ? "RAM SELF-TEST COMPLETE — fixed RAM execution, monitor return and normal restart passed.\n" : "BENCH CHECK COMPLETE — monitor identification and normal diagnostic restart passed.\n") +
                "This does not establish recovery after an interrupted erase/write. Flashing remains unavailable.\nEvidence: " + folder);
        }
        catch (Exception error)
        {
            string reason = error.GetType().Name + ": " + error.Message;
            try { Record("incomplete-or-failed-no-recovery-qualification: " + reason[..Math.Min(reason.Length, 256)]); } catch (IOException) { }
            output.WriteLine("Bench check incomplete. At the menu, disconnect only both ECU positive feeds for 60 seconds, reconnect and wait 10 seconds before option 1. Keep Mongoose powered; do not repeat entry or RAM execution automatically.\nEvidence: " + folder);
            if (error is BenchCleanupException) output.WriteLine("DRIVER CLEANUP UNCERTAIN: disconnect both USB and ECU battery power. Restart MazdaLink after reconnecting. The application will stop.");
            throw;
        }
    }
}
