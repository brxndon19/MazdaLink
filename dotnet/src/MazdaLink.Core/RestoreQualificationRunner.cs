namespace MazdaLink.Core;

public sealed record RestoreCommandObservation(byte Opcode,ushort Sequence,ushort Index,byte Status,uint FlashResult,uint Flags,uint NextUnit,uint Fccs);
public interface IOriginalRestoreChannel:IBenchMonitorChannel
{
    void SendRestoreStep(int index,CancellationToken cancel=default);
    RestoreWireDiagnostics? RestoreDiagnostics=>null;
    RestoreCommandObservation? LastResult=>null;
}
public sealed record RestoreQualificationResult(int Commands,int OriginalUnits,string OriginalSha256,string FinalRead1Sha256,string FinalRead2Sha256,bool FinalMonitorPassed);
public sealed record ControlledStopResult(int Commands,int OriginalUnits,string OriginalSha256,string PartialSha256,bool CalibrationIdErased,bool FinalMonitorPassed);
public sealed record Eb15ControlledStopResult(int Commands,int OriginalUnits,string OriginalSha256,string PartialSha256,bool BootValidityPointerErased,bool FinalMonitorPassed);
public sealed class RestoreQualificationRunner
{
    public RestoreQualificationResult Run(IOriginalRestoreChannel channel,RestoreQualificationPlan plan,Action<string> record,
        CancellationToken cancel=default,Action<int,byte[]>? observeFinalRead=null)
    {
        if(plan.ControlledStage==ControlledRecoveryStage.Prepare)throw new InvalidOperationException("A partial boundary is not a complete restoration.");
        var evidence=Execute(channel,plan,record,cancel,observeFinalRead,null);var final=evidence.Final;
        plan.RequireFullReadback(final[0]);plan.RequireFullReadback(final[1]);
        record(plan.Modified is null?"original-block-restore-dual-readback-and-final-monitor-passed":"intended-block-dual-readback-and-final-monitor-passed");
        return new(evidence.Commands,evidence.Units,plan.Original.OriginalSha256,RomStorage.Hash(final[0]),RomStorage.Hash(final[1]),true);
    }
    public ControlledStopResult RunToControlledStop(IOriginalRestoreChannel channel,RestoreQualificationPlan plan,Action<string> record,
        CancellationToken cancel=default,Action<byte[]>? observePartial=null)
    {
        if(plan.SelectedBlock!=14||plan.ControlledStage!=ControlledRecoveryStage.Prepare)throw new InvalidOperationException("Only the fixed unit-zero plan may create a partial boundary.");
        var evidence=Execute(channel,plan,record,cancel,null,observePartial);plan.RequirePartialImage(evidence.Partial);
        bool absent=evidence.Partial.AsSpan(0xC0091,Zyj2RecoveryPolicy.SoftwareId.Length).ToArray().All(b=>b==255);
        if(!absent)throw new InvalidDataException("Calibration ID was not erased at the controlled boundary.");
        record("completed-unit-zero-partial-image-and-final-monitor-passed-no-active-operation");
        return new(evidence.Commands,evidence.Units,plan.Original.OriginalSha256,RomStorage.Hash(evidence.Partial),true,true);
    }
    public Eb15ControlledStopResult RunEb15ToControlledStop(IOriginalRestoreChannel channel,RestoreQualificationPlan plan,Action<string> record,
        CancellationToken cancel=default,Action<byte[]>? observePartial=null)
    {
        if(plan.SelectedBlock!=15||plan.ControlledStage!=ControlledRecoveryStage.Prepare)throw new InvalidOperationException("Only the fixed EB15 unit-zero plan may create this boundary.");
        var evidence=Execute(channel,plan,record,cancel,null,observePartial);plan.RequirePartialImage(evidence.Partial);
        bool erased=evidence.Partial.AsSpan(0xFFFFC,4).ToArray().All(b=>b==255) && evidence.Partial.AsSpan(0xFF650,19*12+4).ToArray().All(b=>b==255);
        if(!erased)throw new InvalidDataException("EB15 boot/checksum metadata was not erased at the completed boundary.");
        record("completed-eb15-unit-zero-partial-image-and-final-monitor-passed-no-active-operation");
        return new(evidence.Commands,evidence.Units,plan.Original.OriginalSha256,RomStorage.Hash(evidence.Partial),true,true);
    }
    private sealed record ScriptEvidence(int Commands,int Units,byte[][] Final,byte[] Partial);
    private ScriptEvidence Execute(IOriginalRestoreChannel channel,RestoreQualificationPlan plan,Action<string> record,CancellationToken cancel,
        Action<int,byte[]>? observeFinalRead,Action<byte[]>? observePartial)
    {
        bool partial=plan.ControlledStage==ControlledRecoveryStage.Prepare;
        var watch=System.Diagnostics.Stopwatch.StartNew();byte[][] final=partial?[]:[new byte[TargetPolicy.RomSize],new byte[TargetPolicy.RomSize]];
        byte[] partialImage=partial?new byte[TargetPolicy.RomSize]:[];int commands=0,units=0;
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
            if(entry.Phase==RestorePhase.PartialReadback)
            {
                reply.Data.CopyTo(partialImage,entry.Command.Index*512);
                if(entry.Command.Index==2047){plan.RequirePartialImage(partialImage);observePartial?.Invoke(partialImage.ToArray());}
            }
            if(entry.Phase is RestorePhase.FinalReadback1 or RestorePhase.FinalReadback2)
            {
                int pass=entry.Phase==RestorePhase.FinalReadback1?0:1;reply.Data.CopyTo(final[pass],entry.Command.Index*512);
                if(entry.Command.Index==2047){plan.RequireFullReadback(final[pass]);observeFinalRead?.Invoke(pass+1,final[pass].ToArray());}
            }
        }
        if(commands!=plan.Commands.Count||units!=plan.ExpectedUnits)throw new TransportException("Original restore evidence incomplete.");
        return new(commands,units,final,partialImage);
    }
}
