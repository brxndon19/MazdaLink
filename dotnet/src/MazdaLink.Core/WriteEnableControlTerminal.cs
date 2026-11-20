using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Detached-spare fixed write-enable control experiment with intact-app and boot-binding gates.</summary>
public sealed class WriteEnableControlTerminal(Func<IReadTransport> connect, Func<RecoveryBindingPlan, IWriteEnableControlChannel> channelFactory,
    TextReader input, TextWriter output, string dataRoot, Action<int, CancellationToken>? coldWait = null)
{
    public void Run(string originalBackup, CancellationToken cancel)
    {
        var backup = RomStorage.LoadVerifiedBackup(originalBackup);
        var plan = RecoveryBindingPlan.Create(backup);
        output.WriteLine("ZYJ2 DETACHED SPARE — FIXED WRITE-ENABLE CONTROL TEST\n" +
            "Keep the good ECU disconnected. Use ONLY the detached intact ZYJ2 spare and its own complete verified backup. Never run in a vehicle.\n" +
            "Normal identity/RPM, cold entry and exact live boot binding are required before upload.\n" +
            "Every byte of the fixed 752-byte RAM program is verified before execution; it then pulses PD8 once and checks FWE and restoration.\n" +
            "It relies on the examined cold monitor's stored port-D latch baseline; input pin reads are not latch reads. Follow the full power sequence.\n" +
            "It writes only PD8 direction and the high data byte using that known cold baseline. No mux or flash-controller register is written, no algorithm is downloaded, and no erase or ROM programming is available.\n" +
            "Execution/power/adapter failure can leave control uncertain: remove ECU positive power, do not repeat automatically.\n" +
            "Keep Mongoose USB, OBD 12 V, grounds and CAN connected; switch only both ECU positive feeds.\n" +
            "Type ENABLE ZYJ2 to confirm this detached-spare GPIO experiment:");
        if (input.ReadLine() != "ENABLE ZYJ2") throw new InvalidDataException("Write-enable control setup was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder = Path.Combine(Path.GetFullPath(dataRoot), "write-enable-control-tests",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal = new FileStream(Path.Combine(folder, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        IWriteEnableControlChannel? active = null; RecoveryBindingResult? result = null; WriteEnableControlResult? controlResult = null; int sequence = 0;
        string? version = typeof(WriteEnableControlTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string backupHash = RomStorage.Hash(backup.Data);
        void Record(string stage)
        {
            if (++sequence > 32) throw new IOException("Binding evidence limit reached.");
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new { schema = 9, version, sequence, utc = DateTime.UtcNow, stage,
                targetProfile = Zyj2RecoveryPolicy.SoftwareId, backupSha256 = backupHash,
                expectedBootSha256 = plan.ExpectedBootSha256, preservedBytes = RecoveryBindingPlan.PreservedBytes,
                normalDiagnosticsRequiredBeforeEntry = true, applicationCalibrationReadBeforeEntry = true,
                uploadOrExecutionImplemented = true, romEraseOrProgrammingImplemented = false,
                interruptedWriteRecoveryTested = false, bindingResult = result, controlResult,
                controlImageSha256 = Zyj2WriteEnableControlPolicy.ImageSha256, gpioControlImplemented = true, flashControllerWriteImplemented = false,
                controlTransport = active?.ControlDiagnostics,
                bindingTransport = active?.BindingDiagnostics, diagnostics = active?.Diagnostics }, json);
            journal.Write(line); journal.WriteByte((byte)'\n'); journal.Flush(flushToDisk: true);
            if (stage == "entry-window-armed") output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
            if (stage.StartsWith("preserved-boot-bytes-verified: ", StringComparison.Ordinal))
                output.WriteLine(stage + " / " + RecoveryBindingPlan.PreservedBytes);
        }
        void ConfirmNormal()
        {
            var normal = connect();
            try { Zyj2RecoveryPolicy.RequireLiveTarget(normal); }
            finally
            {
                normal.Dispose();
                if (normal.CleanupWarning is not null) throw new BenchCleanupException(normal.CleanupWarning + " Disconnect both USB and ECU battery power before restarting.");
            }
        }
        try
        {
            Record("started-write-enable-control-no-erase-or-programming");
            ConfirmNormal();
            Record("normal-identity-and-engine-stopped-confirmed-before-entry");
            BenchColdStart.Prepare(input, output, Record, cancel, coldWait);
            var channel = channelFactory(plan); active = channel;
            try
            {
                output.WriteLine("Opening the separate write-enable control channel. Keep ECU positive feeds OFF until ENTRY WINDOW.");
                new Zyj2BenchRecovery().IdentifyMonitor(channel, Record, cancel: cancel);
                result = new RecoveryBindingCheck().Run(channel, plan, Record, cancel, observed => result = observed);
                Record("preserved-boot-binding-result-recorded-before-control-upload");
                controlResult = new Zyj2WriteEnableControl().Run(channel, Record, cancel, observed => controlResult = observed);
                Record("write-enable-control-result-recorded-no-flash-qualification");
                output.WriteLine($"Control status {controlResult.Status}: {controlResult.StatusDescription}.\n" +
                    $"During FCCS=0x{controlResult.DuringFccs:X2}, after FCCS=0x{controlResult.AfterFccs:X2}, observations=0x{controlResult.ObservationFlags:X2}.");
                if (!controlResult.Passed) throw new TransportException("Write-enable control did not pass. Remove ECU positive power; no request will be retried.");
                output.WriteLine("WRITE-ENABLE CONTROL RETURNED TO MONITOR. FWE transition and restoration checks passed; no erase/programming was sent.");
            }
            finally
            {
                channel.Dispose();
                if (channel.CleanupWarning is not null) throw new BenchCleanupException(channel.CleanupWarning + " Disconnect both USB and ECU battery power before restarting.");
            }
            BenchColdStart.PrepareNormalRestart(input, output, Record, cancel, coldWait);
            ConfirmNormal();
            Record("normal-restart-confirmed-after-write-enable-control");
            Record("write-enable-control-and-normal-restart-complete-not-flash-recovery-qualified");
            output.WriteLine("WRITE-ENABLE CONTROL COMPLETE — boot binding, GPIO/FWE transition, restoration, monitor return and normal restart passed.\n" +
                "This does not qualify recovery after an interrupted erase/write. Erase and ROM programming remain unavailable.\nEvidence: " + folder);
        }
        catch (Exception error)
        {
            string reason = error.GetType().Name + ": " + error.Message;
            try { Record("incomplete-or-failed-no-recovery-qualification: " + reason[..Math.Min(reason.Length, 256)]); } catch (IOException) { }
            output.WriteLine("Write-enable control incomplete. No request was retried. Keep Mongoose powered; disconnect only both ECU positive feeds for 60 seconds, reconnect and wait 10 seconds before normal option 1.\nEvidence: " + folder);
            if (error is BenchCleanupException) output.WriteLine("DRIVER CLEANUP UNCERTAIN: disconnect both USB and ECU power and restart MazdaLink. The application will stop.");
            throw;
        }
    }
}
