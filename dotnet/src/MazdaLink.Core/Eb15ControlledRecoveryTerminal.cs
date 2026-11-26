using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed class Eb15ControlledRecoveryTerminal(Func<IReadTransport> connect,
    Func<RestoreQualificationPlan,RestoreJournal,IOriginalRestoreChannel> channelFactory,TextReader input,TextWriter output,string dataRoot,
    Action<int,CancellationToken>? coldWait=null)
{
    public void Run(string originalBackup,string originalRestorationFolder,string? resumeFolder,CancellationToken cancel)
    {
        var backup=RomStorage.LoadVerifiedBackup(originalBackup);
        RestoreQualificationPlan? prepare=resumeFolder is null?RestoreQualificationPlan.CreateEb15Controlled(backup,originalRestorationFolder,ControlledRecoveryStage.Prepare):null;
        var recover=RestoreQualificationPlan.CreateEb15Controlled(backup,originalRestorationFolder,ControlledRecoveryStage.Recover);
        Eb15ControlledBoundaryReceipt? receipt=resumeFolder is null?null:Eb15ControlledRecoveryEvidence.RequireBoundary(resumeFolder,backup,originalRestorationFolder);
        string confirmation=resumeFolder is null?"BOUNDARY EB15 RECOVERY ZYJ2":"RESUME EB15 BOUNDARY ZYJ2";
        output.WriteLine("ZYJ2 DETACHED SPARE — EB15 COMPLETED-UNIT BOUNDARY / COLD RECOVERY TEST\n"+
            "Keep the good ECU disconnected. Never use this in a vehicle. Recovery remains experimental and the spare may be left unable to start.\n"+
            "Requires the spare's complete original backup and completed alpha.18 original EB15 restoration folder containing both logs and both final reads. These files establish consistency, not unique ECU identity.\n"+
            "The preparation stage erases EB15, programs and verifies ONLY original unit 0, then verifies the complete planned partial image and returns to the monitor with write-enable low. The checksum table and final boot-validity pointer stay erased. The original EB14, including its calibration ID, stays intact.\n"+
            "Only after BOUNDARY READY and the explicit POWER OFF prompt may you cycle ECU positive power. Recovery uses a NEW cold channel and complete preserved/partial-image checks before a fresh whole-block erase and original restoration. No unit-index resume is permitted.\n"+
            "DO NOT cut power during any erase/program operation. This test does NOT reproduce power loss during an active pulse or an unknown completion. Keep stable power until explicitly prompted. Close earlier MazdaLink/diagnostic instances; use resume only after the earlier run has stopped.\n"+
            "Keep Mongoose USB, OBD 12 V, grounds and CAN powered. Switch only both ECU positive feeds when prompted. Allow roughly 30–40 minutes for a paired run.\nType "+confirmation+" to confirm this detached-spare test:");
        if(input.ReadLine()!=confirmation)throw new InvalidDataException("Controlled recovery test cancelled before adapter open.");
        cancel.ThrowIfCancellationRequested();
        string folder=Path.Combine(Path.GetFullPath(dataRoot),"eb15-controlled-recovery-tests",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        using var events=new FileStream(Path.Combine(folder,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        IOriginalRestoreChannel? active=null;Eb15ControlledStopResult? prepared=null;RestoreQualificationResult? restored=null;
        RestoreWireDiagnostics? prepareTransport=null,recoverTransport=null;bool partialConfirmedAfterColdEntry=false,boundarySaved=receipt is not null;string phase="setup";int sequence=0;
        string? version=typeof(Eb15ControlledRecoveryTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string? resumedBoundarySha256=resumeFolder is null?null:RomStorage.Hash(File.ReadAllBytes(Path.Combine(resumeFolder,"boundary.json")));
        string originalRestorationEventSha256=RomStorage.Hash(File.ReadAllBytes(Path.Combine(originalRestorationFolder,"events.jsonl")));
        string originalRestorationJournalSha256=RomStorage.Hash(File.ReadAllBytes(Path.Combine(originalRestorationFolder,"transaction.jsonl")));
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        void Record(string stage)
        {
            if(++sequence>512)throw new IOException("Controlled recovery evidence bound exceeded.");
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(new{schema=15,version,sequence,utc=DateTime.UtcNow,phase,stage,
                targetProfile=Zyj2RecoveryPolicy.SoftwareId,backupSha256=recover.Original.OriginalSha256,kernelSha256=Eb15OriginalRestoreKernel.ImageSha256,kernelBytes=Eb15OriginalRestoreKernel.ImageLength,
                eraseBlock=15,blockStart=0xE0000,controlledStopUnit=0,activeOperationPowerCutImplemented=false,activeOperationPowerLossQualified=false,modifiedFlashingEnabled=false,
                originalRestorationEventSha256,originalRestorationJournalSha256,resumedBoundarySha256,boundarySaved,partialConfirmedAfterColdEntry,prepared,restored,prepareTransport,recoverTransport,
                lastResult=active?.LastResult,transport=active?.RestoreDiagnostics,diagnostics=active?.Diagnostics},json);
            events.Write(line);events.WriteByte(10);events.Flush(flushToDisk:true);
            if(stage=="entry-window-armed")output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
            if(stage.Contains("-blocks-verified:",StringComparison.Ordinal)||stage.StartsWith("original-units-verified:",StringComparison.Ordinal))output.WriteLine(phase+": "+stage);
        }
        void Progress(string stage)
        {
            Record(stage);
            if(phase=="recover" && stage=="BeforeErase-blocks-verified: 2048")
            {partialConfirmedAfterColdEntry=true;Record("cold-entry-full-partial-image-confirmed-before-recovery-erase");}
        }
        void Normal()
        {
            var link=connect();try{Zyj2RecoveryPolicy.RequireLiveTarget(link);}finally{link.Dispose();if(link.CleanupWarning is not null)throw new BenchCleanupException(link.CleanupWarning);}
        }
        T Execute<T>(RestoreQualificationPlan plan,string name,Func<IOriginalRestoreChannel,T> run)
        {
            string path=Path.Combine(folder,name);Directory.CreateDirectory(path);
            using var journal=new RestoreJournal(Path.Combine(path,"transaction.jsonl"),plan.Original);
            var channel=channelFactory(plan,journal);active=channel;
            try{new Zyj2BenchRecovery().IdentifyMonitor(channel,Record,cancel:cancel);return run(channel);}
            finally
            {
                if(name=="prepare")prepareTransport=channel.RestoreDiagnostics;else recoverTransport=channel.RestoreDiagnostics;
                channel.Dispose();if(channel.CleanupWarning is not null)throw new BenchCleanupException(channel.CleanupWarning);
            }
        }
        try
        {
            Record("started-controlled-boundary-test-no-active-operation-power-cut");
            if(prepare is not null)
            {
                phase="prepare";Normal();Record("intact-normal-preflight-passed-before-controlled-preparation");
                BenchColdStart.Prepare(input,output,Record,cancel,coldWait);
                prepared=Execute(prepare,"prepare",channel=>new RestoreQualificationRunner().RunEb15ToControlledStop(channel,prepare,Progress,cancel,bytes=>
                {RomStorage.SaveNew(Path.Combine(folder,"partial.read"),bytes,prepare.RequirePartialImage);Record("exact-partial-image-durably-saved");}));
                active=null;
                receipt=new(1,15,prepare.Original.OriginalSha256,prepared.PartialSha256,Eb15OriginalRestoreKernel.ImageSha256,1,true,true,false,originalRestorationEventSha256,originalRestorationJournalSha256);
                RomStorage.SaveNew(Path.Combine(folder,"boundary.json"),JsonSerializer.SerializeToUtf8Bytes(receipt));
                Eb15ControlledRecoveryEvidence.RequireBoundary(folder,backup,originalRestorationFolder);boundarySaved=true;
                Record("completed-unit-boundary-receipt-and-journal-verified-no-operation-pending");
            }
            if(resumeFolder is not null)
            {
                Directory.CreateDirectory(Path.Combine(folder,"prepare"));
                foreach(string name in new[]{"boundary.json","partial.read"})RomStorage.SaveNew(Path.Combine(folder,name),File.ReadAllBytes(Path.Combine(resumeFolder,name)));
                RomStorage.SaveNew(Path.Combine(folder,"prepare","transaction.jsonl"),File.ReadAllBytes(Path.Combine(resumeFolder,"prepare","transaction.jsonl")));
                Eb15ControlledRecoveryEvidence.RequireBoundary(folder,backup,originalRestorationFolder);Record("validated-boundary-artifacts-copied-for-recovery-only");
            }
            phase="boundary";
            output.WriteLine(resumeFolder is null?
                "BOUNDARY READY: exactly unit 0 is programmed and verified; the EB15 checksum table and boot-validity pointer remain erased. Monitor return and low write-enable were checked. No chip operation is pending.\nEvidence: "+folder:
                "SAVED EB15 BOUNDARY VALIDATED: the receipt describes one completed original unit with erased boot/checksum metadata. The live image will be checked after fresh cold entry, before any erase. The earlier run must be stopped/closed.\nEvidence: "+resumeFolder);
            output.WriteLine("Normal application diagnostics are not expected in the planned partial state. Keep this folder if the app closes; menu 25 requires its validated receipt and an exact live boundary match.");
            Record("boundary-ready-before-operator-power-cycle");
            phase="recover";BenchColdStart.Prepare(input,output,Record,cancel,coldWait);
            restored=Execute(recover,"recover",channel=>new RestoreQualificationRunner().Run(channel,recover,Progress,cancel,(pass,bytes)=>
            {RomStorage.SaveNew(Path.Combine(folder,"final-pass-"+pass+".read"),bytes,recover.Original.RequireFullReadback);Record("restored-original-final-read-durably-saved: "+pass);}));
            active=null;
            if(!partialConfirmedAfterColdEntry)throw new InvalidDataException("Cold-entry partial-image evidence is incomplete.");
            phase="normal-restart";BenchColdStart.PrepareNormalRestart(input,output,Record,cancel,coldWait);Normal();
            Record("normal-restart-confirmed-after-controlled-cold-recovery");Record("controlled-eb15-completed-unit-recovery-passed-active-power-loss-not-qualified");
            output.WriteLine("CONTROLLED BOUNDARY / COLD RECOVERY PASSED: partial image confirmed after cold entry; fresh erase, original restoration, both full reads, monitor return and normal restart passed.\n"+
                "This does not qualify power loss during an active erase/program operation or modified flashing.\nEvidence: "+folder);
        }
        catch(Exception error)
        {
            try{Record("incomplete: "+error.GetType().Name);}catch(IOException){}
            output.WriteLine("CONTROLLED RECOVERY INCOMPLETE. No request was resent. "+(restored is not null?"Original readbacks passed; a later stage failed. Do not erase again solely because of that later failure.":"The spare may intentionally contain an incomplete EB15 boot/checksum metadata.")+"\n"+
                "Keep the good ECU disconnected and save the complete evidence folder. Do not interrupt an active operation or repeat an individual unit. If power must be removed after a stopped run, leave the spare powered for at least 15 seconds first.\n"+
                (boundarySaved?"A validated boundary receipt is available; menu 25 requires that the live ECU still exactly matches that boundary. Stop and send the evidence if recovery already changed EB15. Recovery remains experimental.\n":"A completed boundary receipt is not available. Stop and send the evidence for review before another erase attempt.\n")+"Evidence: "+folder);
            throw;
        }
    }
}
