namespace MazdaLink.Core;

public interface ITransactionTrialChannel:IBenchMonitorChannel
{
    void SendTrialStep(int index);
    TransactionTrialDiagnostics? TrialDiagnostics=>null;
}
public sealed record TransactionTrialResult(int BootBytes,int KernelBytes,int ReadBytes,int StagedUnits,int Commands,string ReadbackSha256,bool MatchesOriginal);
public sealed class TransactionTrialRunner
{
    public TransactionTrialResult Run(ITransactionTrialChannel channel,TransactionTrialPlan plan,Action<string> record,
        CancellationToken cancel=default,Action<TransactionReply>? observeReply=null,Action<byte[]>? observeReadback=null)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();byte[] capture=new byte[TargetPolicy.RomSize];
        int boot=0,kernel=0,blocks=0,staged=0,commands=0;
        record("native-read-stage-script-started-no-erase-or-programming");
        for(int index=0;index<plan.Count;index++)
        {
            cancel.ThrowIfCancellationRequested();
            if(clock.Elapsed>=TimeSpan.FromMinutes(15))throw new TransportException("Native transaction trial exceeded its fixed host budget. No request was resent.");
            var step=plan[index];channel.SendTrialStep(index);
            TransactionReplyCollector? collector=step.Kind==TransactionReplyKind.Command?new(step.Command!):null;
            var replyClock=System.Diagnostics.Stopwatch.StartNew();int frames=0;
            while(true)
            {
                cancel.ThrowIfCancellationRequested();
                if(replyClock.Elapsed>=TimeSpan.FromSeconds(collector is null?2:5))throw new TransportException("Native transaction reply timed out. Outcome is unknown; no request was resent.");
                var frame=channel.Read(25);if(frame is null){Thread.Sleep(1);continue;}
                if(++frames>256)throw new TransportException("Native transaction receive bound exceeded.");
                if(collector is not null){collector.Accept(frame);if(collector.Complete)break;continue;}
                if(frame.Status==0x101&&step.Kind!=TransactionReplyKind.Echo)continue;
                if(!step.Matches(frame))throw new TransportException("Native transaction reply/byte mismatch. No request was resent.");
                break;
            }
            if(step.BootByte && ++boot==28928)record("preserved-boot-bytes-verified-before-native-installation");
            if(step.KernelByte && ++kernel==NativeTransactionPolicy.ImageLength)record("every-native-transaction-code-byte-verified");
            if(collector is null)continue;
            var reply=collector.Reply!;observeReply?.Invoke(reply);commands++;
            if(!reply.ReadStageSuccess)throw new TransportException($"Native transaction rejected: opcode={reply.Opcode:X2}, status={reply.Status}, full FPFR={reply.FlashResult:X8}. No request was resent.");
            if(step.Command!.Opcode==TransactionOpcode.ReadBlock)
            {
                if(step.Command.Index!=blocks)throw new TransportException("Full capture block order changed.");
                reply.Data.CopyTo(capture,blocks++*512);
                if(blocks%32==0)record("native-rom-blocks-verified: "+blocks);
                if(blocks==2048)
                {
                    observeReadback?.Invoke(capture.ToArray());
                    if(!capture.AsSpan().SequenceEqual(plan.Original))throw new InvalidDataException("Current full ROM differs from the selected original backup. Staging was not attempted.");
                    record("complete-current-rom-matched-original-before-ram-staging");
                }
            }
            if(step.Command.Opcode==TransactionOpcode.StageEcho){staged++;record("original-unit-staged-and-echoed-in-ram: "+step.Command.Index);}
        }
        if(boot!=28928||kernel!=NativeTransactionPolicy.ImageLength||blocks!=2048||staged!=8||commands!=plan.Commands.Count)
            throw new TransportException("Native transaction trial evidence is incomplete.");
        record("native-read-stage-and-final-monitor-check-complete");
        return new(boot,kernel,capture.Length,staged,commands,RomStorage.Hash(capture),true);
    }
}
