using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed class RestoreQualificationTerminal(Func<IReadTransport> connect,
    Func<RestoreQualificationPlan,RestoreJournal,IOriginalRestoreChannel> channelFactory,TextReader input,TextWriter output,string dataRoot,
    Action<int,CancellationToken>? coldWait=null)
{
    public void Run(string originalBackup,string prerequisite,bool recovery,CancellationToken cancel)
        =>RunCore(originalBackup,prerequisite,recovery,false,cancel);
    public void RunEb15(string originalBackup,string prerequisite,bool recovery,CancellationToken cancel)
        =>RunCore(originalBackup,prerequisite,recovery,true,cancel);
    private void RunCore(string originalBackup,string prerequisite,bool recovery,bool eb15,CancellationToken cancel)
    {
        var backup=RomStorage.LoadVerifiedBackup(originalBackup);
        var plan=eb15?RestoreQualificationPlan.CreateEb15(backup,prerequisite,recovery):RestoreQualificationPlan.Create(backup,prerequisite,recovery);
        string block=eb15?"EB15":"EB14";
        string confirmation=eb15?(recovery?"RECOVER EB15 ORIGINAL ZYJ2":"RESTORE EB15 ORIGINAL ZYJ2"):(recovery?"RECOVER ORIGINAL ZYJ2":"RESTORE ORIGINAL ZYJ2");
        output.WriteLine($"ZYJ2 DETACHED SPARE — ORIGINAL {block} RESTORATION QUALIFICATION\n"+
            "This is the first hardware erase/program qualification path. It can leave the spare unable to start; recovery has NOT been proved. Keep the good ECU disconnected. Never run in a vehicle.\n"+
            (eb15?"Use this spare's own complete original backup and completed alpha.16 controlled-recovery run folder, including both journals, boundary capture/receipt and both final reads. This does not prove unique ECU identity.\n":
            "Use only this spare's own complete verified original backup and its completed alpha.14 native-transaction events.jsonl. The log is consistency evidence, not proof of unique ECU identity.\n")+
            (eb15?"This ERASES EB15 E0000..FFFFF, including checksum words and the boot-validity pointer. All 1024 original units are restored once; unit 1023 containing the pointer is LAST. Every byte outside EB15, including EB14, must stay unchanged. No modified file is accepted.\n":
            "This ERASES the complete 128 KiB EB14 block, then programs all 1024 original 128-byte units once. Boot and every other block, including EB15 checksum words, must remain unchanged. No modified file is accepted.\n")+
            $"Intent is flushed before every destructive request. No ambiguous request is resent. Any recovery attempt starts with cold binding and a complete new {block} erase; it cannot resume a unit index.\n"+
            $"Recovery mode deliberately skips normal identity/RPM and allows existing {block} differences. It still requires exact preserved regions. Recovery remains experimental; EB15 cold recovery and active-operation power loss are unqualified.\n"+
            "Keep stable bench power throughout execution. DO NOT intentionally interrupt power during this first restoration test. Keep Mongoose USB, OBD 12 V, grounds and CAN powered; switch only both ECU positive feeds when prompted.\n"+
            "The full script may take around 20 minutes. Do not use other diagnostic software.\nType "+confirmation+" to confirm the detached spare, original backup and risk:");
        if(input.ReadLine()!=confirmation)throw new InvalidDataException("Original restoration was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder=Path.Combine(Path.GetFullPath(dataRoot),eb15?"eb15-original-restore-tests":"original-restore-tests",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        using var events=new FileStream(Path.Combine(folder,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var durable=new RestoreJournal(Path.Combine(folder,"transaction.jsonl"),plan.Original);
        IOriginalRestoreChannel? active=null;RestoreQualificationResult? result=null;int sequence=0;
        string? version=typeof(RestoreQualificationTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        void Record(string stage)
        {
            if(++sequence>256)throw new IOException("Restore evidence bound exceeded.");
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(new {schema=eb15?14:12,version,sequence,utc=DateTime.UtcNow,stage,recovery,eraseBlock=plan.SelectedBlock,blockStart=plan.Original.SelectedStart,
                targetProfile=Zyj2RecoveryPolicy.SoftwareId,backupSha256=plan.Original.OriginalSha256,blockSha256=plan.Original.BlockSha256,
                expectedBootSha256=plan.Original.PreservedBootSha256,kernelSha256=plan.KernelSha256,kernelBytes=plan.KernelBytes,
                originalOnlyEraseProgrammingImplemented=true,modifiedFlashingEnabled=false,interruptedWriteRecoveryQualified=false,
                normalDiagnosticsRequiredBeforeEntry=!recovery,result,lastResult=active?.LastResult,transport=active?.RestoreDiagnostics,diagnostics=active?.Diagnostics},json);
            events.Write(line);events.WriteByte(10);events.Flush(flushToDisk:true);
            if(stage=="entry-window-armed")output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
            if(stage.Contains("-blocks-verified:",StringComparison.Ordinal)||stage.StartsWith("original-units-verified:",StringComparison.Ordinal))output.WriteLine(stage);
        }
        void Normal()
        {
            var link=connect();try{Zyj2RecoveryPolicy.RequireLiveTarget(link);}finally{link.Dispose();if(link.CleanupWarning is not null)throw new BenchCleanupException(link.CleanupWarning);}
        }
        try
        {
            Record("started-original-only-qualification-recovery-unproved");
            if(!recovery){Normal();Record("intact-normal-preflight-passed-before-first-erase-test");}
            BenchColdStart.Prepare(input,output,Record,cancel,coldWait);
            var channel=channelFactory(plan,durable);active=channel;
            try
            {
                new Zyj2BenchRecovery().IdentifyMonitor(channel,Record,cancel:cancel);
                result=new RestoreQualificationRunner().Run(channel,plan,Record,cancel,(pass,bytes)=>
                {
                    RomStorage.SaveNew(Path.Combine(folder,"final-pass-"+pass+".read"),bytes,plan.Original.RequireFullReadback);
                    Record("original-exact-final-readback-durably-saved: "+pass);
                });Record("original-restore-and-final-monitor-passed");
            }
            finally{channel.Dispose();if(channel.CleanupWarning is not null)throw new BenchCleanupException(channel.CleanupWarning);}
            BenchColdStart.PrepareNormalRestart(input,output,Record,cancel,coldWait);Normal();
            Record("normal-restart-confirmed-after-original-restoration");Record(eb15?"eb15-original-restoration-complete-cold-and-active-recovery-not-qualified":"original-restoration-complete-interrupted-recovery-not-qualified");
            output.WriteLine("ORIGINAL RESTORATION AND NORMAL RESTART PASSED. Both final 1 MiB reads exactly match the original.\n"+
                "This does not qualify interrupted-write recovery or modified flashing. Keep the complete original backup and this evidence folder.\nEvidence: "+folder);
        }
        catch(Exception error)
        {
            try{Record("incomplete: "+error.GetType().Name);}catch(IOException){}
            string outcome=result is not null?"Original readbacks and monitor return passed, but a later restart/cleanup stage did not complete. Do not erase again solely because of that later failure.":
                active?.RestoreDiagnostics?.EraseAttempted==true?$"The spare may have erased or partially written {block} data, including boot/checksum metadata if EB15 was selected.":"Erase was not attempted.";
            output.WriteLine("ORIGINAL QUALIFICATION INCOMPLETE. No request was resent. "+outcome+"\n"+
                "Keep the good ECU disconnected. Do not repeat individual commands or try a modified ROM. Save events.jsonl AND transaction.jsonl for review.\n"+
                "If power must be removed after a stopped run, first leave the spare powered for at least 15 seconds. A recovery attempt must use the separate fresh-bind/full-erase workflow; it remains unqualified.\nEvidence: "+folder);
            throw;
        }
    }
}
