using System.Reflection;
using System.Text.Json;
namespace MazdaLink.Core;

public sealed class ModifiedFlashTerminal(Func<IReadTransport> connect,
    Func<ModifiedFlashSequence,RestoreQualificationPlan,ModifiedFlashJournal,IModifiedFlashChannel> channelFactory,
    TextReader input,TextWriter output,string dataRoot,Action<int,CancellationToken>? coldWait=null)
{
    public void Run(string originalPath,string? preparedPath,bool recovery,CancellationToken cancel)
    {
        var original=RomStorage.LoadVerifiedBackup(originalPath);
        byte[] prepared=recovery?original.Data.ToArray():File.ReadAllBytes(preparedPath??throw new InvalidDataException("Choose a checksum-prepared .bin or .rom file."));
        var transaction=ModifiedFlashSequence.Create(original,prepared,recovery);var first=transaction.First();
        string confirmation=recovery?"RECOVER BOTH ORIGINAL ZYJ2":"FLASH MODIFIED SPARE ZYJ2";
        output.WriteLine("ZYJ2 DETACHED SPARE — EXPERIMENTAL TWO-BLOCK FLASH\n"+
            "Use only the detached ZYJ2EA000ZYV5050 / ZYC3 spare. Keep the good ECU disconnected. Never run this on the car.\n"+
            (recovery?"This cold recovery attempt restores BOTH complete blocks to the original backup. It skips normal application diagnostics. It cannot repair boot/program damage.\n":
            "This writes your prepared .bin/.rom: EB14 first, then the EB15 checksum block; the boot-validity pointer is restored in the LAST unit. The current image must be a checksum-valid supported calibration.\n")+
            "Do not cycle power between blocks. The same monitor connection is retained. Stable bench power is essential; do not intentionally interrupt any operation.\n"+
            "Single-block original writes and completed-unit cold entry were observed on this spare. Combined modified flashing and power loss during an active pulse are NOT hardware-qualified. This can leave the ECU unable to start.\n"+
            "Recovery always uses fresh cold binding and whole-block erases from unit zero. No ambiguous command is resent. Boot-region damage or failed monitor entry may require another recovery method.\n"+
            "A checksum pass does not validate tuning tables or engine safety. Keep Mongoose USB, OBD 12 V, grounds and CAN powered; switch only both ECU positive feeds when prompted.\n"+
            "Allow around 30–40 minutes. Close other diagnostic software.\n"+
            "Original SHA256: "+transaction.OriginalSha256+"\nTarget SHA256: "+transaction.IntendedSha256+"\nType "+confirmation+" to confirm the detached spare and risk:");
        if(input.ReadLine()!=confirmation)throw new InvalidDataException("Modified spare transaction was not confirmed. No adapter was opened.");
        cancel.ThrowIfCancellationRequested();
        string folder=Path.Combine(Path.GetFullPath(dataRoot),"modified-spare-flashes",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+transaction.Id);
        Directory.CreateDirectory(folder);
        using var events=new FileStream(Path.Combine(folder,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var journal=new ModifiedFlashJournal(Path.Combine(folder,"transaction.jsonl"),transaction);
        IModifiedFlashChannel? active=null;RestoreQualificationPlan plan=first;bool flashed=false,eraseSeen=false;int sequence=0;
        string? version=typeof(ModifiedFlashTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        void Record(string stage)
        {
            eraseSeen|=active?.RestoreDiagnostics?.EraseAttempted==true;
            if(++sequence>320)throw new IOException("Combined flash evidence bound exceeded.");
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(new {schema=16,version,sequence,utc=DateTime.UtcNow,stage,
                transaction=transaction.Id,recovery,originalSha256=transaction.OriginalSha256,intendedSha256=transaction.IntendedSha256,
                eraseBlock=plan.SelectedBlock,kernelSha256=plan.KernelSha256,kernelBytes=plan.KernelBytes,
                modifiedSpareWritingImplemented=true,combinedHardwareQualified=false,activePowerLossRecoveryQualified=false,
                eraseAttempted=eraseSeen,lastResult=active?.LastResult,transport=active?.RestoreDiagnostics},json);
            events.Write(line);events.WriteByte(10);events.Flush(flushToDisk:true);
            if(stage=="entry-window-armed")output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW; leave them connected until the restart prompt. Waiting 20 seconds...");
            if(stage.Contains("-blocks-verified:",StringComparison.Ordinal)||stage.StartsWith("original-units-verified:",StringComparison.Ordinal))
                output.WriteLine($"EB{plan.SelectedBlock}: "+stage.Replace("original-units","intended-units",StringComparison.Ordinal));
        }
        void Normal()
        {
            var link=connect();try{Zyj2RecoveryPolicy.RequireLiveTarget(link);}finally{link.Dispose();if(link.CleanupWarning is not null)throw new BenchCleanupException(link.CleanupWarning);}
        }
        try
        {
            Record("started-detached-spare-two-block-transaction");
            if(!recovery){Normal();Record("normal-preflight-passed");}
            BenchColdStart.Prepare(input,output,Record,cancel,coldWait);
            var channel=channelFactory(transaction,first,journal);active=channel;
            try
            {
                new Zyj2BenchRecovery().IdentifyMonitor(channel,Record,cancel:cancel);
                for(int block=14;block<=15;block++)
                {
                    if(block==15)
                    {
                        eraseSeen|=channel.RestoreDiagnostics?.EraseAttempted==true;
                        plan=channel.AdvanceToEb15();Record("eb14-complete-transitioned-to-eb15-without-power-cycle");
                    }
                    new RestoreQualificationRunner().Run(channel,plan,Record,cancel,(pass,bytes)=>
                    {
                        RomStorage.SaveNew(Path.Combine(folder,$"eb{plan.SelectedBlock}-pass-{pass}.read"),bytes,plan.RequireFullReadback);
                        Record($"eb{plan.SelectedBlock}-exact-full-readback-saved: {pass}");
                    });Record($"eb{block}-complete-and-monitor-returned");
                }
                // The second block's exact final image must be the entire intended ROM, including all checksums.
                byte[] final=plan.Modified!.FinalImage();
                if(RomStorage.Hash(final)!=transaction.IntendedSha256||Zyj2Integrity.Check(final).Any(x=>!x.Valid))throw new InvalidDataException("Final combined image/checksum verification failed.");
                flashed=true;Record("both-blocks-and-intended-dual-full-readbacks-passed");
            }
            finally{channel.Dispose();if(channel.CleanupWarning is not null)throw new BenchCleanupException(channel.CleanupWarning);}
            BenchColdStart.PrepareNormalRestart(input,output,Record,cancel,coldWait);Normal();
            Record("normal-restart-passed-after-combined-transaction");Record("complete-detached-spare-active-power-loss-unqualified");
            output.WriteLine("BOTH BLOCKS VERIFIED AND NORMAL RESTART PASSED. Both final 1 MiB reads exactly match the target.\n"+
                "Keep this complete evidence folder for review. This does not qualify the good ECU, tuning safety or recovery from an active-operation power loss.\nEvidence: "+folder);
        }
        catch(Exception error)
        {
            try{Record("incomplete: "+error.GetType().Name);}catch(IOException){}
            output.WriteLine("TWO-BLOCK TRANSACTION STOPPED. No request was resent. "+
                (flashed?"Both target readbacks and monitor return passed; a later cleanup/restart failed. Do not erase again solely for that later failure.":
                 eraseSeen?"Either calibration block may be incomplete. Keep the good ECU disconnected. Save the entire run folder; recovery must cold-bind and re-erase BOTH complete blocks to the original.":"Erase was not attempted.")+"\n"+
                "After a stopped run, leave the spare powered for at least 15 seconds before removing power. Do not repeat an individual request or resume a unit. Recovery remains experimental.\nEvidence: "+folder);
            throw;
        }
    }
}
