namespace MazdaLink.Core;

public sealed record RestoreCommandObservation(byte Opcode,ushort Sequence,ushort Index,byte Status,uint FlashResult,uint Flags,uint NextUnit,uint Fccs);
public interface IOriginalRestoreChannel:IBenchMonitorChannel
{
    void SendRestoreStep(int index,CancellationToken cancel=default);
    RestoreWireDiagnostics? RestoreDiagnostics=>null;
    RestoreCommandObservation? LastResult=>null;
}
public sealed record RestoreQualificationResult(int Commands,int OriginalUnits,string OriginalSha256,string FinalRead1Sha256,string FinalRead2Sha256,bool FinalMonitorPassed);
public sealed class RestoreQualificationRunner
{
    public RestoreQualificationResult Run(IOriginalRestoreChannel channel,RestoreQualificationPlan plan,Action<string> record,
        CancellationToken cancel=default,Action<int,byte[]>? observeFinalRead=null)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();byte[][] final=[new byte[TargetPolicy.RomSize],new byte[TargetPolicy.RomSize]];int commands=0,units=0;
        for(int index=0;index<plan.Count;index++)
        {
            cancel.ThrowIfCancellationRequested();if(watch.Elapsed>=TimeSpan.FromMinutes(40))throw new TransportException("Original restore host budget exceeded. No request was resent.");
            var step=plan[index];channel.SendRestoreStep(index,cancel);
            TransactionReplyCollector? collector=step.Kind==TransactionReplyKind.Command?new(step.Command!):null;
            var timer=System.Diagnostics.Stopwatch.StartNew();int frames=0;
            while(true)
            {
                cancel.ThrowIfCancellationRequested();int seconds=step.Command?.Opcode==TransactionOpcode.Erase?15:collector is null?2:5;
                if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))throw new TransportException("Original restore reply timed out. Outcome unknown; no request was resent.");
                var frame=channel.Read(25);if(frame is null){Thread.Sleep(1);continue;}
                if(++frames>256)throw new TransportException("Original restore frame bound exceeded.");
                if(collector is not null){collector.Accept(frame);if(collector.Complete)break;continue;}
                if(frame.Status==0x101 && step.Kind!=TransactionReplyKind.Echo)continue;
                if(!step.Matches(frame))throw new TransportException("Original restore wire reply mismatch.");break;
            }
            if(collector is null)continue;
            var entry=plan.CommandFor(index)!;var reply=collector.Reply!;
            RestoreQualificationGate.RequireResult(entry,reply);plan.RequireData(entry,reply);commands++;
            if(entry.Phase==RestorePhase.UnitReadback && ++units%32==0)record("original-units-verified: "+units);
            if(entry.Phase==RestorePhase.Erase)record("erase-result-and-busy-callback-observed-before-full-blank-preserved-readback");
            if(entry.Command.Opcode==TransactionOpcode.ReadBlock && (entry.Command.Index+1)%128==0)record(entry.Phase+"-blocks-verified: "+(entry.Command.Index+1));
            if(entry.Phase is RestorePhase.FinalReadback1 or RestorePhase.FinalReadback2)
            {
                int pass=entry.Phase==RestorePhase.FinalReadback1?0:1;reply.Data.CopyTo(final[pass],entry.Command.Index*512);
                if(entry.Command.Index==2047){plan.Original.RequireFullReadback(final[pass]);observeFinalRead?.Invoke(pass+1,final[pass].ToArray());}
            }
        }
        if(commands!=plan.Commands.Count||units!=1024)throw new TransportException("Original restore evidence incomplete.");
        plan.Original.RequireFullReadback(final[0]);plan.Original.RequireFullReadback(final[1]);
        record("original-block-restore-dual-readback-and-final-monitor-passed");
        return new(commands,units,plan.Original.OriginalSha256,RomStorage.Hash(final[0]),RomStorage.Hash(final[1]),true);
    }
}
