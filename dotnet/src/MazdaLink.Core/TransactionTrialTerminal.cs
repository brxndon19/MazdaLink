using System.Reflection;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed class TransactionTrialTerminal(Func<IReadTransport> connect,Func<TransactionTrialPlan,ITransactionTrialChannel> channelFactory,
    TextReader input,TextWriter output,string dataRoot,Action<int,CancellationToken>? coldWait=null)
{
    public void Run(string originalBackup,CancellationToken cancel)
    {
        var backup=RomStorage.LoadVerifiedBackup(originalBackup);var plan=TransactionTrialPlan.Create(backup);
        output.WriteLine("ZYJ2 DETACHED SPARE — NATIVE READ / RAM STAGING CHECK\n"+
            "Use ONLY the detached ZYJ2 spare and its own complete verified backup. Keep the good ECU disconnected. Never run in a vehicle.\n"+
            "This verifies the preserved boot bytes and every byte of one fixed RAM transaction program, then streams the complete 1 MiB ROM and compares it to your original.\n"+
            "Only after an exact match, eight original 128-byte units are staged and echoed in volatile RAM. Both protected MCU algorithms are initialized with the fixed RAM service callback.\n"+
            "The embedded kernel has no erase/program operation. FWE stays low; no ROM is changed. Actual flash busy and interrupted-write recovery remain unqualified.\n"+
            "No normal application/RPM preflight is used before cold entry; confirm the detached spare physically. Keep USB, OBD 12 V, grounds and CAN powered; switch only both ECU positive feeds.\n"+
            "Type TRANSACTION ZYJ2 to confirm this read/staging trial:");
        if(input.ReadLine()!="TRANSACTION ZYJ2")throw new InvalidDataException("Transaction trial cancelled before adapter open.");
        cancel.ThrowIfCancellationRequested();
        string folder=Path.Combine(Path.GetFullPath(dataRoot),"native-transaction-checks",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var journal=new FileStream(Path.Combine(folder,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        ITransactionTrialChannel? active=null;TransactionTrialResult? result=null;object? lastReply=null;string? capturedHash=null;int sequence=0;
        string? version=typeof(TransactionTrialTerminal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        void Record(string stage)
        {
            if(++sequence>128)throw new IOException("Transaction evidence bound exceeded.");
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(new {schema=11,version,sequence,utc=DateTime.UtcNow,stage,
                targetProfile=Zyj2RecoveryPolicy.SoftwareId,backupSha256=RomStorage.Hash(backup.Data),expectedBootSha256=plan.Binding.ExpectedBootSha256,
                kernelSha256=NativeTransactionPolicy.ImageSha256,kernelBytes=NativeTransactionPolicy.ImageLength,
                hardwareFlashEnabled=false,romEraseOrProgrammingImplemented=false,actualFlashBusyTested=false,interruptedWriteRecoveryTested=false,
                normalDiagnosticsRequiredBeforeEntry=false,applicationCalibrationReadBeforeEntry=false,
                result,lastReply,capturedHash,transport=active?.TrialDiagnostics,diagnostics=active?.Diagnostics},json);
            journal.Write(line);journal.WriteByte(10);journal.Flush(flushToDisk:true);
            if(stage=="entry-window-armed")output.WriteLine("ENTRY WINDOW: reconnect BOTH spare ECU positive feeds NOW and leave them connected. Waiting 20 seconds...");
            if(stage.StartsWith("native-rom-blocks-verified:",StringComparison.Ordinal))output.WriteLine(stage+" / 2048");
        }
        try
        {
            Record("started-no-erase-or-program-capability");BenchColdStart.Prepare(input,output,Record,cancel,coldWait);
            var channel=channelFactory(plan);active=channel;
            try
            {
                new Zyj2BenchRecovery().IdentifyMonitor(channel,Record,cancel:cancel);
                result=new TransactionTrialRunner().Run(channel,plan,Record,cancel,reply=>
                {
                    // Deliberately exclude reply.Data: ROM bytes must never enter evidence logs.
                    lastReply=new {reply.Opcode,reply.Sequence,reply.Status,reply.FlashResult,reply.DataCrc32,reply.Flags,reply.NextUnit,reply.Index,reply.Fccs};
                    if(reply.Opcode!=(byte)TransactionOpcode.ReadBlock || !reply.ReadStageSuccess)Record("native-command-result");
                },bytes=>
                {
                    string path=Path.Combine(folder,"verification.read");
                    using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(flushToDisk:true);}
                    if(!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)))throw new IOException("Verification capture disk readback mismatch.");
                    capturedHash=RomStorage.Hash(bytes);Record("verification-capture-saved-original-backup-unchanged");
                });
                Record("read-stage-trial-and-final-monitor-passed");
            }
            finally
            {
                channel.Dispose();
                if(channel.CleanupWarning is not null)throw new BenchCleanupException(channel.CleanupWarning);
            }
            BenchColdStart.PrepareNormalRestart(input,output,Record,cancel,coldWait);
            var normal=connect();
            try{Zyj2RecoveryPolicy.RequireLiveTarget(normal);}
            finally{normal.Dispose();if(normal.CleanupWarning is not null)throw new BenchCleanupException(normal.CleanupWarning);}
            Record("normal-restart-confirmed-after-native-trial");Record("complete-no-flash-recovery-qualification");
            output.WriteLine("NATIVE READ / RAM STAGING PASSED: full ROM matched your original, eight RAM units matched, final monitor and normal restart passed.\n"+
                "No flash operation was sent. This is not interrupted-write recovery qualification.\nEvidence: "+folder);
        }
        catch(Exception error)
        {
            try{Record("incomplete: "+error.GetType().Name);}catch(IOException){}
            output.WriteLine("Transaction trial incomplete. No request was resent. Keep Mongoose powered; disconnect only both ECU positive feeds for 60 seconds, reconnect and wait 10 seconds before option 1.\nEvidence: "+folder);
            throw;
        }
    }
}
