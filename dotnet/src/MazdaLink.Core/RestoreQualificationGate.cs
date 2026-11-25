namespace MazdaLink.Core;

public sealed record RestoreWireDiagnostics(int SentSteps,int BootBytes,int KernelBytes,int StateBytes,int CommandsCompleted,int OriginalUnitsVerified,bool AwaitingReply,bool Failed,bool EraseAttempted);
/// <summary>Native wire gate: intent is durably committed immediately before the
/// destructive execute request, and every comparison gates all later requests.</summary>
public sealed class RestoreQualificationGate
{
    private readonly RestoreQualificationPlan plan;
    private readonly IRestoreJournal journal;
    private int next,boot,kernel,state,completed,commandBytes,stagingBytes,frames,units;
    private TransactionWireStep? pending;
    private TransactionCommand? current;
    private TransactionReplyCollector? collector;
    private bool failed,eraseAttempted;
    public RestoreQualificationGate(RestoreQualificationPlan plan,IRestoreJournal journal)
    {this.plan=plan;this.journal=journal;journal.Commit("started");}
    public RestoreCommandObservation? LastResult { get; private set; }
    public RestoreWireDiagnostics Diagnostics=>new(next,boot,kernel,state,completed,units,pending is not null,failed,eraseAttempted);
    public TransactionWireStep Begin(int index)
    {
        try
        {
            if(failed||pending is not null||index!=next||index<0||index>=plan.Count)throw new TransportException("Original restore sequence/reply gate failed. No retry is permitted.");
            var step=plan[index];
            if(index>=plan.InstallationEnd && (boot!=28928||kernel!=OriginalRestoreKernel.ImageLength||state!=48))throw new TransportException("Every boot, kernel and state byte must be verified first.");
            if(step.Command is not null && !ReferenceEquals(current,step.Command)){current=step.Command;commandBytes=0;stagingBytes=0;}
            if(step.Kind==TransactionReplyKind.Command)
            {
                var cmd=step.Command!;
                if(commandBytes!=16||stagingBytes!=(cmd.Opcode==TransactionOpcode.Program?128:0))throw new TransportException("Every descriptor and original staging byte must be verified before execution.");
                if(cmd.Opcode==TransactionOpcode.Erase){journal.Commit("erase-intent");eraseAttempted=true;}
                if(cmd.Opcode==TransactionOpcode.Program)journal.Commit("unit-intent",cmd.Index);
                collector=new(cmd);
            }
            next++;frames=0;pending=step;return step;
        }
        catch{failed=true;throw;}
    }
    public void Observe(RecoveryFrame frame)
    {
        if(pending is null)return;
        try
        {
            if(++frames>256)throw new TransportException("Original restore receive bound exceeded.");
            if(pending.Kind==TransactionReplyKind.Command)
            {
                collector!.Accept(frame);if(!collector.Complete)return;
                var entry=plan.CommandFor(next-1)!;var reply=collector.Reply!;
                LastResult=new(reply.Opcode,reply.Sequence,reply.Index,reply.Status,reply.FlashResult,reply.Flags,reply.NextUnit,reply.Fccs);
                RequireResult(entry,reply);plan.RequireData(entry,reply);completed++;
                if(entry.Phase==RestorePhase.BeforeErase && entry.Command.Index==2047)journal.Commit("bound");
                if(entry.Phase==RestorePhase.EraseReadback && entry.Command.Index==2047)journal.Commit("erase-verified");
                if(entry.Phase==RestorePhase.UnitReadback){units++;journal.Commit("unit-verified",entry.Command.Index);}
                if(entry.Phase==RestorePhase.FinalReadback1 && entry.Command.Index==2047)journal.Commit("full-readback-1");
            }
            else
            {
                if(frame.Status==0x101 && pending.Kind!=TransactionReplyKind.Echo)return;
                if(!pending.Matches(frame))throw new TransportException("Original restore reply or byte readback mismatch.");
                if(pending.BootByte)boot++;if(pending.KernelByte)kernel++;if(pending.StateByte)state++;
                if(pending.CommandByte)commandBytes++;if(pending.StagingByte)stagingBytes++;
                if(next==plan.Count){if(units!=1024||completed!=plan.Commands.Count)throw new TransportException("Restore evidence incomplete.");journal.Commit("complete");}
            }
            pending=null;
        }
        catch{failed=true;throw;}
    }
    public static void RequireResult(RestoreCommand entry,TransactionReply reply)
    {
        bool before=entry.Phase is RestorePhase.Initialize or RestorePhase.BeforeErase;
        uint flags=before?1u:99u;
        uint next=entry.Phase is RestorePhase.Program or RestorePhase.UnitReadback?(uint)entry.Command.Index+1:
            entry.Phase is RestorePhase.FinalReadback1 or RestorePhase.FinalReadback2 or RestorePhase.Finish?1024u:0u;
        if(reply.Status!=0||reply.FlashResult!=0||reply.Fccs!=0||reply.Flags!=flags||reply.NextUnit!=next)
            throw new TransportException($"Original restore rejected: status={reply.Status}, full FPFR={reply.FlashResult:X8}, flags={reply.Flags:X8}, next={reply.NextUnit}. Stop; no request was resent.");
    }
    public void Fail()=>failed=true;
}
