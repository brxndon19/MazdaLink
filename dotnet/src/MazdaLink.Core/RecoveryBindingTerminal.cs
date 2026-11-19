using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Detached-spare read-only entry/binding probe with no normal diagnostic preflight.</summary>
public sealed class RecoveryBindingTerminal(Func<IReadTransport> connect, Func<RecoveryBindingPlan, IRecoveryBindingChannel> channelFactory,
    TextReader input, TextWriter output, string dataRoot, Action<int, CancellationToken>? coldWait = null)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        var plan = RecoveryBindingPlan.Create(backup);
        output.WriteLine("ZYJ2 DETACHED SPARE — READ-ONLY RECOVERY BOOT BINDING\n" +
            "Use ONLY the detached ZYJ2 spare and its own complete verified backup. Keep the good ECU disconnected. Never use this probe in a vehicle.\n" +
            "This probe deliberately skips normal application identification and RPM checks before entry. The detached bench setup must be confirmed physically.\n" +
            "It enters the monitor directly and reads the fixed preserved boot region byte-for-byte. Only the monitor read pointer is selected; no upload, execution, flash-controller write, erase, ROM programming or ECU reset is available.\n" +
            "Matching this region is not a unique serial-number identity or proof of recovery after an interrupted write. The application is still intact for this test.\n" +
            "Keep Mongoose USB, OBD 12 V, grounds and CAN connected; switch only both ECU positive feeds.\n" +
            "Type BIND ZYJ2 to confirm the detached spare, its own backup and this entry-only setup:");
        if (input.ReadLine() != "BIND ZYJ2") throw new InvalidDataException("Detached-spare binding setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), "recovery-binding-tests",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        IRecoveryBindingChannel? active = null; RecoveryBindingResult? result = null; int sequence = 0;
        string? version = typeof(RecoveryBindingTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string backupHash = RomStorage.Hash(backup.Data);
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Binding evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { schema = 8, version, sequence, utc = DateTime.UtcNow, stage,
                targetProfile = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = backupHash,
                expectedBootSha256 = plan.ExpectedBootSha256, preservedBytes = RecoveryBindingPlan.PreservedBytes,
                normalDiagnosticsRequiredBeforeEntry = false, applicationCalibrationReadBeforeEntry = false,
                uploadOrExecutionImplemented = false, romEraseOrProgrammingImplemented = false,
                interruptedWriteRecoveryTested = false, bindingResult = result,
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
                output.WriteLine("Opening the read-only binding channel. Keep ECU positive feeds OFF until ENTRY WINDOW.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
                result = new RecoveryBindingCheck().Run(channel, plan, Record, cancel, observed => result = observed);
                Record("preserved-boot-binding-result-recorded-no-flash-qualification");
                output.WriteLine($"PRESERVED BOOT MATCHED: {result.VerifiedBytes:N0} bytes verified against the selected backup.\n" +
                    "Monitor return passed. No application software ID was required for entry/binding. No flash operation was sent.");
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
            Record("normal-restart-confirmed-after-binding");
            Record("read-only-recovery-binding-and-normal-restart-complete-not-interrupted-write-recovery");
            output.WriteLine("RECOVERY BOOT BINDING COMPLETE — direct monitor entry, exact preserved-region match, monitor return and normal restart passed.\n" +
                "This does not qualify recovery after an interrupted erase/write. Erase and ROM programming remain unavailable.\nEvidence: " + folder);
        }
        catch (Exception error)
        {
            string reason = error.GetType().Name + ": " + error.Message;
            try { Record("incomplete-or-failed-no-recovery-qualification: " + reason[..Math.Min(reason.Length, 256)]); } catch (IOException) { }
            output.WriteLine("Binding probe incomplete. No request was retried. Keep Mongoose powered; disconnect only both ECU positive feeds for 60 seconds, reconnect and wait 10 seconds before normal option 1.\nEvidence: " + folder);
            if (error is BenchCleanupException) output.WriteLine("DRIVER CLEANUP UNCERTAIN: disconnect both USB and ECU power and restart MazdaLink. The application will stop.");
            throw;
        }
    }
}
