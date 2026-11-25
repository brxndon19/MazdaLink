namespace MazdaLink.Core;

public sealed class Terminal(Func<IReadTransport> connect, TextReader input, TextWriter output, string dataRoot,
    Func<IBenchMonitorChannel>? benchMonitor = null, Func<IFixedRamSelfTestChannel>? ramSelfTest = null, Func<IProtectedInitializationChannel>? protectedInitialization = null,
    Func<RecoveryBindingPlan, IRecoveryBindingChannel>? recoveryBinding = null,
    Func<RecoveryBindingPlan, IWriteEnableControlChannel>? writeEnableControl = null,
    Func<RecoveryBindingPlan, IRecoveryRehearsalChannel>? recoveryRehearsal = null,
    Func<TransactionTrialPlan,ITransactionTrialChannel>? transactionTrial = null)
{
    public const string Help = """
MazdaLink — Mazda 2 DE / ZY-VE
Raw reads: no fixed calibration whitelist (including ZYJ2EA000ZYV5050).
Checksum preparation: ZY87EC000ZYV5050 only; other checksum layouts remain unverified.
Adapter: Mongoose-Plus ISO/CAN3, manufacturer's 32-bit J2534 04.04 driver

Commands:
  drivers                     List registered Mongoose J2534 drivers.
  identify                    Show the connected ECU's reported calibration.
  read                        Read twice and save an immutable local backup.
  export <original.rom> <new.rom>
                              Export an exact copy of a complete MazdaLink backup.
  prepare <original.rom> <edited.rom> <new.rom>
                              ZY87 only: prepare calibration-only edits and verify all 19 checksums.
  validate <original.rom> <edited.rom>
                              ZY87 only: check permitted changes and all 19 checksums.
  bench-recovery-check <original.rom>
                              ZYJ2 spare only: identify its boot monitor and check normal restart.
                              Requires a complete MazdaLink backup and a detached bench ECU.
  ram-self-test <original.rom>
                              ZYJ2 spare only: upload/read back/execute one fixed volatile RAM test.
                              Query resident hardware class, flash status and programming-pin snapshot; confirm monitor return and normal restart.
                              Experimental and unqualified; no flash-controller writes or ROM programming.
  protected-flash-init <original.rom>
                              ZYJ2 SH7058S spare: fixed protected download/initialization test.
                              Controller selection/SCO writes; no PD8 change, erase or ROM programming.
                              Requires INIT ZYJ2 confirmation; experimental and unqualified.
  recovery-binding-check <original.rom>
                              Detached ZYJ2 spare only: direct entry and exact preserved-boot match.
                              No normal diagnostic/RPM preflight; requires BIND ZYJ2 confirmation.
                              No upload, execute, flash-controller write, erase or ROM programming.
  write-enable-check <original.rom>
                              Detached intact ZYJ2 spare: fixed GPIO/FWE pulse and restoration.
                              Normal preflight and exact live boot binding required; ENABLE ZYJ2.
                              No flash-controller write, algorithm download, erase or programming.
  recovery-rehearsal <original.rom>
                              Detached ZYJ2 spare: application-independent boot binding, fixed
                              algorithm initialization and direct RAM watchdog callback rehearsal.
                              REHEARSE ZYJ2 required. No write-enable change, erase or programming.
  original-restore-plan <original.rom>
                              Offline unchanged-backup EB14 recipe only; no hardware or flashing.
  native-transaction-check <original.rom>
                              Detached ZYJ2 spare: full-ROM CRC32 stream and original-unit RAM staging.
                              TRANSACTION ZYJ2 required. No erase or programming capability.
  flash                       Unavailable: Mazda 2 write/recovery protocol is unverified.
  status                      Show read policy, checksum support, storage and programming status.
  license                     Show licence and attribution information.
  help                        Show this help.

Use read with ignition ON and engine OFF. Keep the entire backup folder.
Modified ROMs must be edited externally. A checksum pass does not establish tuning safety.
Raw reading uses one bounded read-only protocol; matching backups do not prove checksum or flashing compatibility.
No protocol override, flash erase, ROM write, or ECU reset is available.
The ZYJ2 bench check sends only fixed monitor-entry and identification messages.
The separate RAM self-test accepts no external kernel, address or executable.
It does not qualify recovery after interrupted flashing or enable a flash kernel.
Keep OBD 12 V and USB powered; switch only ECU positive feeds as prompted.
Bench entry uses a 60-second ECU-off wait BEFORE its 20-second reconnect window.
Normal restart: POWER OFF for a timed 60 seconds, then POWER ON and a 10-second startup wait.
Bench logs include adapter status and measured timing, without frame payloads.
""";
    public int Run(string[] args, CancellationToken cancel = default)
    {
        if (args.Length > 0) return Execute(args, cancel);
        output.WriteLine("MazdaLink — Mazda 2 DE");
        output.WriteLine($"Raw reads: no fixed calibration whitelist. Checksum preparation: {TargetPolicy.SoftwareId} only.\nStorage: {Path.GetFullPath(dataRoot)}\nFlashing: unavailable — write/recovery procedure is unverified.");
        while (!cancel.IsCancellationRequested)
        {
            output.WriteLine("\n1  Identify ECU\n2  Read ECU and save backup\n3  Export backup as .rom\n4  Prepare an externally edited ROM\n5  Validate modified ROM\n6  Flash ROM back — unavailable\n7  Help\n8  Licence / credits\n9  ZYJ2 spare: bench monitor identification check\n10 ZYJ2 spare: fixed volatile RAM self-test — experimental\n11 ZYJ2 spare: protected flash-algorithm initialization — experimental\n12 ZYJ2 detached spare: read-only recovery boot binding — experimental\n13 ZYJ2 detached spare: write-enable control and restoration — experimental\n14 ZYJ2 detached spare: recovery kernel rehearsal — experimental\n15 Original-backup restore plan — offline only\n16 ZYJ2 detached spare: native full-ROM read and RAM staging — experimental\n0  Exit");
            output.Write("> ");
            string? choice = input.ReadLine()?.Trim();
            if (choice is null or "0") return 0;
            string[] command;
            switch (choice)
            {
                case "1": command = ["identify"]; break;
                case "2": command = ["read"]; break;
                case "3": command = ["export", Ask("Original backup original.rom"), Ask("New .rom destination")]; break;
                case "4": command = ["prepare", Ask("Original backup original.rom"), Ask("Externally edited ROM"), Ask("New corrected .rom destination")]; break;
                case "5": command = ["validate", Ask("Original backup original.rom"), Ask("Modified ROM")]; break;
                case "6": command = ["flash"]; break;
                case "7": command = ["help"]; break;
                case "8": command = ["license"]; break;
                case "9": command = ["bench-recovery-check", Ask("ZYJ2 backup original.rom")]; break;
                case "10": command = ["ram-self-test", Ask("ZYJ2 backup original.rom")]; break;
                case "11": command = ["protected-flash-init", Ask("ZYJ2 backup original.rom")]; break;
                case "12": command = ["recovery-binding-check", Ask("ZYJ2 spare backup original.rom")]; break;
                case "13": command = ["write-enable-check", Ask("ZYJ2 spare backup original.rom")]; break;
                case "14": command = ["recovery-rehearsal", Ask("ZYJ2 spare backup original.rom")]; break;
                case "15": command = ["original-restore-plan", Ask("ZYJ2 spare backup original.rom")]; break;
                case "16": command = ["native-transaction-check",Ask("ZYJ2 spare backup original.rom")]; break;
                default: output.WriteLine("Choose a listed number."); continue;
            }
            Execute(command, cancel); // Each hardware action creates/disposes a fresh connection; never retries.
        }
        output.WriteLine("Stopped. No request was retried.");
        return 130;
    }
    private string Ask(string prompt) { output.Write(prompt + ": "); return (input.ReadLine() ?? "").Trim().Trim('"'); }
    public int Execute(string[] args, CancellationToken cancel = default)
    {
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (args.Length == 1 && args[0] is "help" or "--help" or "-h") { output.WriteLine(Help); return 0; }
            if (args.Length == 1 && args[0] == "license")
            {
                output.WriteLine("MazdaLink is GPL-3.0-or-later. Selected low-level J2534, checksum and security routines are adapted from SpeedLink. Original credits are preserved in ATTRIBUTIONS.md; full licence and exact source accompany every release. https://github.com/brxndon19/MazdaLink"); return 0;
            }
            if (args.Length == 1 && args[0] == "status")
            {
                output.WriteLine($"MazdaLink\nRead identity policy: reported calibration; no whitelist\nChecksum preparation: {TargetPolicy.SoftwareId} only\nROM: 1 MiB\nRead block: 512 bytes\nProcess: {IntPtr.Size * 8}-bit\nRuntime: {Environment.Version}\nFlashing: unavailable\nProtected initialization: ZYJ2 SH7058S spare only; experimental, no erase or programming\nRecovery boot binding: detached ZYJ2 spare only; read-only and experimental\nOther-vehicle workflows: absent\nStorage: {Path.GetFullPath(dataRoot)}"); return 0;
            }
            if (args.Length > 0 && args[0] == "flash")
                throw new InvalidDataException("Flashing is unavailable. This ECU's erase/write/recovery procedure has not been verified. No adapter was opened and no ECU command was sent.");
            if (args.Length == 2 && args[0] == "bench-recovery-check")
            {
                if (benchMonitor is null) throw new TransportException("The bench monitor backend is unavailable. No adapter was opened.");
                new BenchRecoveryTerminal(connect, benchMonitor, input, output, dataRoot).Run(args[1], cancel);
                return 0;
            }
            if (args.Length == 2 && args[0] == "ram-self-test")
            {
                if (ramSelfTest is null) throw new TransportException("The separate fixed RAM self-test backend is unavailable. No adapter was opened.");
                new BenchRecoveryTerminal(connect, () => ramSelfTest(), input, output, dataRoot, runRamSelfTest: true).Run(args[1], cancel);
                return 0;
            }
            if (args.Length == 2 && args[0] == "protected-flash-init")
            {
                if (protectedInitialization is null) throw new TransportException("The separate protected initialization backend is unavailable. No adapter was opened.");
                new BenchRecoveryTerminal(connect, () => protectedInitialization(), input, output, dataRoot,
                    runProtectedInitialization: true).Run(args[1], cancel);
                return 0;
            }
            if(args.Length==2 && args[0]=="native-transaction-check")
            {
                if(transactionTrial is null)throw new TransportException("Native transaction trial backend unavailable. No adapter was opened.");
                new TransactionTrialTerminal(connect,transactionTrial,input,output,dataRoot).Run(args[1],cancel);return 0;
            }
            if(args.Length==2 && args[0]=="original-restore-plan")
            {
                var backup=RomStorage.LoadVerifiedBackup(args[1]);
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(OriginalBlockRestorePlan.Create(backup,backup.Data).Report(),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
                return 0;
            }
            if(args.Length==2 && args[0]=="recovery-rehearsal")
            {
                if(recoveryRehearsal is null) throw new TransportException("The separate recovery rehearsal backend is unavailable. No adapter was opened.");
                new RecoveryRehearsalTerminal(connect,recoveryRehearsal,input,output,dataRoot).Run(args[1],cancel);
                return 0;
            }
            if (args.Length == 2 && args[0] == "write-enable-check")
            {
                if (writeEnableControl is null) throw new TransportException("The separate write-enable control backend is unavailable. No adapter was opened.");
                new WriteEnableControlTerminal(connect, writeEnableControl, input, output, dataRoot).Run(args[1], cancel);
                return 0;
            }
            if (args.Length == 2 && args[0] == "recovery-binding-check")
            {
                if (recoveryBinding is null) throw new TransportException("The separate read-only recovery binding backend is unavailable. No adapter was opened.");
                new RecoveryBindingTerminal(connect, recoveryBinding, input, output, dataRoot).Run(args[1], cancel);
                return 0;
            }
            if (args.Length == 1 && args[0] is "read" or "identify")
            {
                using var link = connect();
                if (args[0] == "identify") output.WriteLine("ECU reported calibration: " + TargetPolicy.Identify(link));
                else
                {
                    int last = -1;
                    string backup = new ReadWorkflow().Capture(link, dataRoot, (pass, bytes) =>
                    {
                        int percent = (int)(100L * bytes / TargetPolicy.RomSize);
                        if (percent == last) return; last = percent;
                        output.WriteLine($"Pass {pass}/2: {percent}%");
                    }, cancel);
                    output.WriteLine($"READ COMPLETE — two full reads and disk copies match.\nReported calibration: {RomStorage.LoadVerifiedBackup(backup).ReportedCalibration}\nOriginal: {backup}\nKeep the entire backup folder. Use Export to save another raw .rom copy. Raw backup verification does not verify checksum or programming compatibility.");
                }
                return 0;
            }
            if (args.Length == 3 && args[0] == "export")
            { RomStorage.Export(args[1], args[2]); output.WriteLine("EXPORTED — exact raw bytes verified: " + Path.GetFullPath(args[2])); return 0; }
            if (args.Length == 4 && args[0] == "prepare")
            { RomEditor.SaveNew(args[1], args[2], args[3]); output.WriteLine("PREPARED — calibration-only changes, all 19 checksums and disk readback verified: " + Path.GetFullPath(args[3]) + "\nThis does not enable flashing or establish tuning safety."); return 0; }
            if (args.Length == 3 && args[0] == "validate")
            {
                var changes = RomEditor.Validate(args[1], args[2]);
                output.WriteLine($"VALIDATED — {changes.Calibration:N0} calibration bytes changed; all 19 checksums pass.\nFlashing remains unavailable. Tuning safety and factory provenance are not established."); return 0;
            }
            throw new ArgumentException("Unknown command or arguments. Use help. Protocol, calibration and other-vehicle overrides are not supported.");
        }
        catch (OperationCanceledException) { output.WriteLine("Stopped. No verified export was published from the interrupted operation."); return 130; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { output.WriteLine("STOPPED: " + error.Message); return 1; }
    }
}
