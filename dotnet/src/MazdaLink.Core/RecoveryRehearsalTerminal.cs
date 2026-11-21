using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Detached-spare read-only entry/binding probe with no normal diagnostic preflight.</summary>
public sealed class RecoveryRehearsalTerminal(Func<IReadTransport> connect, Func<RecoveryBindingPlan, IRecoveryRehearsalChannel> channelFactory,
    TextReader input, TextWriter output, string dataRoot, Action<int, CancellationToken>? coldWait = null)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        var plan = RecoveryBindingPlan.Create(backup);
        output.WriteLine("ZYJ2 DETACHED SPARE — RECOVERY KERNEL REHEARSAL\n" +
            "Use ONLY the detached ZYJ2 spare and its own complete verified original backup. Keep the good ECU disconnected. Never run in a vehicle.\n" +
            "This path deliberately skips normal application identity/RPM and live calibration-ID reads before binding/execution. Confirm the detached bench setup physically.\n" +
            "After exact boot binding it verifies and executes one fixed 1404-byte RAM program. It initializes both MCU algorithms with a RAM service callback and directly rehearses that callback.\n" +
            "It keeps FWE low and changes no PD8 setting. No erase/program entry or programming key is present. This does not exercise actual flash busy or interrupted-write recovery.\n" +
            "It services the existing timer/PB15 output from RAM. The port-B high-byte input latches are normalized once; their earlier stored values cannot be read or restored. Other physical pins and output bits are preserved. Complete the final power cycle.\n" +
            "Keep Mongoose USB, OBD 12 V, grounds and CAN connected; switch only both ECU positive feeds.\n" +
            "Type REHEARSE ZYJ2 to confirm this detached-spare preparation test:");
        if (input.ReadLine() != "REHEARSE ZYJ2") throw new InvalidDataException("Recovery rehearsal setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), "recovery-rehearsals",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        IRecoveryRehearsalChannel? active = null; RecoveryBindingResult? result = null; RecoveryRehearsalResult? rehearsalResult = null; int sequence = 0;
        string? version = typeof(RecoveryRehearsalTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string backupHash = RomStorage.Hash(backup.Data);
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Binding evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { schema = 10, version, sequence, utc = DateTime.UtcNow, stage,
                targetProfile = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = backupHash,
                expectedBootSha256 = plan.ExpectedBootSha256, preservedBytes = RecoveryBindingPlan.PreservedBytes,
                normalDiagnosticsRequiredBeforeEntry = false, applicationCalibrationReadBeforeEntry = false,
                uploadOrExecutionImplemented = true, romEraseOrProgrammingImplemented = false,
                interruptedWriteRecoveryTested = false, bindingResult = result, rehearsalResult,
                rehearsalImageSha256 = Zyj2RecoveryRehearsalPolicy.ImageSha256, flashControllerInitializationImplemented = true,
                writeEnableChanged = false, portBInputLatchNormalizationImplemented = true, actualFlashBusyTested = false,
                rehearsalTransport = active?.RehearsalDiagnostics,
                bindingTransport = active?.BindingDiagnostics, diagnostics = active?.Diagnostics }, json);
            journal.Write(line); journal.WriteByte((byte)'\n'); journal.Flush(flushToDisk: true);
            if (stage == "entry-window-armed") output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
            if (stage.StartsWith("preserved-boot-bytes-verified: ", StringComparison.Ordinal))
                output.WriteLine(stage + " / " + RecoveryBindingPlan.PreservedBytes);
        }
        try
        {
            Record("started-without-normal-application-preflight");
            BenchColdStart.Prepare(input, output, Record, cancel, coldWait);
            var channel = channelFactory(plan); active = channel;
            try
            {
                output.WriteLine("Opening the separate recovery rehearsal channel. Keep ECU positive feeds OFF until ENTRY WINDOW.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
                result = new RecoveryBindingCheck().Run(channel, plan, Record, cancel, observed => result = observed);
                Record("preserved-boot-binding-result-recorded-before-rehearsal-upload");
                rehearsalResult = new Zyj2RecoveryRehearsal().Run(channel, Record, cancel, observed => rehearsalResult = observed);
                Record("recovery-rehearsal-result-recorded-no-erase-or-programming");
                output.WriteLine($"RECOVERY REHEARSAL RETURNED TO MONITOR. Status={rehearsalResult.Status}, initialization low byte={rehearsalResult.LastInitializationResultLowByte:X2}, RAM-serviced overflows={rehearsalResult.RamServiceOverflows}, observations={rehearsalResult.ObservationFlags:X2}.\n" +
                    "No application calibration ID was required. No erase or programming operation was sent.");
            }
            finally
            {
                channel.Dispose();
                if (channel.CleanupWarning is not null) throw new BenchCleanupException(channel.CleanupWarning + " Disconnect both USB and ECU battery power before restarting.");
            }
            BenchColdStart.PrepareNormalRestart(input, output, Record, cancel, coldWait);
            var normal = connect();
            try { Zyj2RecoveryPolicy.RequireLiveTarget(normal); }
            finally
            {
                normal.Dispose();
                if (normal.CleanupWarning is not null) throw new BenchCleanupException(normal.CleanupWarning + " Disconnect both USB and ECU battery power before restarting.");
            }
            Record("normal-restart-confirmed-after-recovery-rehearsal");
            Record("recovery-rehearsal-and-normal-restart-complete-not-flash-recovery-qualified");
            output.WriteLine("RECOVERY REHEARSAL COMPLETE — boot binding, algorithm initialization, direct RAM service, cleanup, monitor return and normal restart passed.\n" +
                "This does not qualify recovery after an interrupted erase/write. Erase and ROM programming remain unavailable.\nEvidence: " + folder);
        }
        catch (Exception error)
        {
            string reason = error.GetType().Name + ": " + error.Message;
            try { Record("incomplete-or-failed-no-recovery-qualification: " + reason[..Math.Min(reason.Length, 256)]); } catch (IOException) { }
            output.WriteLine("Recovery rehearsal incomplete. No request was retried. Keep Mongoose powered; disconnect only both ECU positive feeds for 60 seconds, reconnect and wait 10 seconds before normal option 1.\nEvidence: " + folder);
            if (error is BenchCleanupException) output.WriteLine("DRIVER CLEANUP UNCERTAIN: disconnect both USB and ECU power and restart MazdaLink. The application will stop.");
            throw;
        }
    }
}
