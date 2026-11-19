using System.Text.Json;
using System.Reflection;

namespace MazdaLink.Core;

public sealed class BenchRecoveryTerminal(Func<IReadTransport> connect, Func<IBenchMonitorChannel> monitor,
    TextReader input, TextWriter output, string dataRoot, bool runRamSelfTest = false,
    Action<int, CancellationToken>? coldWait = null, bool runProtectedInitialization = false)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        if (runProtectedInitialization && runRamSelfTest) throw new InvalidOperationException("Choose one fixed RAM capability.");
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        Zyj2RecoveryPolicy.RequireBackup(backup);
        string confirmation = runProtectedInitialization ? "INIT ZYJ2" : runRamSelfTest ? "RAM ZYJ2" : "BENCH ZYJ2";
        output.WriteLine("ZYJ2 SPARE ECU — " + (runProtectedInitialization ? "PROTECTED FLASH-ALGORITHM INITIALIZATION" : runRamSelfTest ? "FIXED VOLATILE RAM SELF-TEST" : "BENCH MONITOR IDENTIFICATION ONLY"));
        output.WriteLine("Use the detached ZYJ2 spare on your bench harness. Keep the good ECU disconnected.");
        if (runProtectedInitialization)
            output.WriteLine("This uploads only the fixed 936-byte protected initialization program and verifies every byte before one execution.\n" +
                "It requires SH7058S class, normal mapping, low FWE and known live CAN timing before controller changes.\n" +
                "It writes controller selection/download registers to copy the chip's two flash algorithms into RAM and calls only their initialization entries.\n" +
                "PD8 is not changed; the existing watchdog helper is serviced between phases. The erase/program entries and programming-enable key are absent. Controller selection/key cleanup, monitor return and normal restart are checked separately.\n" +
                "This controller-initialization experiment is physically unqualified. It does not qualify erase, ROM programming or interrupted-write recovery.");
        else if (runRamSelfTest)
            output.WriteLine("This uploads only MazdaLink's fixed 120-byte RAM test, verifies every byte and reads resident hardware class, flash status and PD8 state.\n" +
                "The resident class query restores SYSCR2. This read-only self-test changes no GPIO or flash-control register and downloads no flash algorithm.");
        else output.WriteLine("This checks cold-start monitor entry and normal restart only. No upload, execute, memory write, controller operation, erase or ROM programming is sent by this workflow.");
        output.WriteLine("Keep Mongoose USB and OBD 12 V powered. Switch only both ECU positive feeds; keep grounds and CAN connected.\n" +
            $"Type {confirmation} to confirm the detached spare and bench setup:");
        if (input.ReadLine() != confirmation) throw new InvalidDataException("Bench setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), runProtectedInitialization ? "protected-initialization-tests" : runRamSelfTest ? "ram-self-tests" : "bench-checks", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        IBenchMonitorChannel? activeChannel = null;
        RamSelfTestResult? ramResult = null;
        ProtectedInitializationResult? initializationResult = null;
        string? version = typeof(BenchRecoveryTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        int sequence = 0;
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Bench evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { schema = 7, version, sequence, utc = DateTime.UtcNow, stage,
                calibration = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = RomStorage.Hash(backup.Data),
                flashOperationsImplemented = false, interruptedFlashRecoveryTested = false,
                fixedRamSelfTest = runRamSelfTest, ramResult,
                protectedInitialization = runProtectedInitialization, initializationResult,
                ramTransport = (activeChannel as IFixedRamSelfTestChannel)?.SelfTestDiagnostics ?? (activeChannel as IProtectedInitializationChannel)?.InitializationDiagnostics,
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
                if (runProtectedInitialization && channel is not IProtectedInitializationChannel)
                    throw new TransportException("The separate protected initialization backend is unavailable.");
                output.WriteLine("Opening bench channel. Keep ECU positive feeds disconnected until ENTRY WINDOW.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
                if (runRamSelfTest)
                {
                    ramResult = new Zyj2RamSelfTest().Run((IFixedRamSelfTestChannel)channel, Record, cancel);
                    Record("fixed-ram-self-test-result-recorded-no-flash");
                    output.WriteLine($"RAM SELF-TEST RETURNED TO MONITOR. {ramResult.VerifiedBytes} bytes verified before execution.\n" +
                        $"Resident hardware class: {ramResult.HardwareClass} — {ramResult.HardwareClassName}. Erase geometry must match the confirmed device.\n" +
                        $"Read-only status: FWE={(ramResult.FlashWriteEnableObserved ? 1 : 0)}, FLER={(ramResult.FlashErrorObserved ? 1 : 0)}, MAT=0x{ramResult.MatSelection:X2}. These observations do not authorize flashing.\n" +
                        $"PD8 snapshot: alternate-function={(ramResult.Pd8AlternateFunctionSelected ? 1 : 0)}, output-direction={(ramResult.Pd8OutputDirection ? 1 : 0)}, data-high={(ramResult.Pd8DataHigh ? 1 : 0)}, pin-high={(ramResult.Pd8PinHigh ? 1 : 0)}. No pin setting was changed.");
                }
                if (runProtectedInitialization)
                {
                    initializationResult = new Zyj2ProtectedInitialization().Run((IProtectedInitializationChannel)channel, Record, cancel, observed => initializationResult = observed);
                    Record("protected-initialization-result-recorded-no-erase-or-programming");
                    output.WriteLine($"Protected initialization status {initializationResult.Status}: {initializationResult.StatusDescription}.\n" +
                        $"Program DPFR=0x{initializationResult.ProgrammingDownloadResult:X2}, erase DPFR=0x{initializationResult.ErasingDownloadResult:X2}, last initialization result low byte=0x{initializationResult.LastInitializationResultLowByte:X2}.");
                    if (!initializationResult.ProtectionChecksPassed)
                        throw new TransportException("Protected initialization did not pass. No erase/program entry was called and no request will be retried.");
                    output.WriteLine("PROTECTED INITIALIZATION RETURNED TO MONITOR. Both initialization returns and controller protection/cleanup checks passed. This is not a flash or recovery qualification.");
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
            if (runProtectedInitialization) Record("protected-initialization-and-normal-restart-complete-not-flash-qualified");
            Record("identification-check-complete-not-flash-recovery-qualified");
            output.WriteLine((runProtectedInitialization ? "PROTECTED INITIALIZATION COMPLETE — download, initialization, monitor return and normal restart passed.\n" : runRamSelfTest ? "RAM SELF-TEST COMPLETE — fixed RAM execution, monitor return and normal restart passed.\n" : "BENCH CHECK COMPLETE — monitor identification and normal diagnostic restart passed.\n") +
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
