using MazdaLink.Core;
namespace MazdaLink.J2534;

/// <summary>Dedicated two-block spare capability. Transition retains monitor and adapter power;
/// it never starts another entry pulse, resets the ECU or reuses a failed gate.</summary>
public sealed class NativeModifiedFlashChannel:IModifiedFlashChannel
{
    private readonly NativeBenchMonitorChannel inner;
    private readonly ModifiedFlashSequence transaction;
    private readonly ModifiedFlashJournal journal;
    private RestoreQualificationPlan plan;
    private bool advanced;
    public NativeModifiedFlashChannel(J2534Driver driver,ModifiedFlashSequence transaction,RestoreQualificationPlan first,ModifiedFlashJournal journal)
    {
        if(first.Modified is null||first.Modified.TransactionId!=transaction.Id||first.SelectedBlock!=14)
            throw new InvalidDataException("The dedicated modified capability requires its first fixed EB14 plan.");
        this.transaction=transaction;this.journal=journal;plan=first;journal.BindStep(plan);
        inner=new(driver,new RestoreQualificationGate(plan,journal));
    }
    public RestoreQualificationPlan AdvanceToEb15()
    {
        if(advanced||inner.RestoreDiagnostics is not {Failed:false,AwaitingReply:false}||inner.RestoreDiagnostics.SentSteps!=plan.Count)
            throw new TransportException("The first block must finish before the one permitted EB15 transition.");
        try
        {
            var next=transaction.Second(plan);journal.BindStep(next);
            inner.AdvanceModifiedRestore(new(next,journal));advanced=true;plan=next;return next;
        }
        catch{inner.FailOriginalRestore();throw;}
    }
    public string? CleanupWarning=>inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics=>inner.Diagnostics;
    public RestoreWireDiagnostics? RestoreDiagnostics=>inner.RestoreDiagnostics;
    public RestoreCommandObservation? LastResult=>inner.RestoreLastResult;
    public void Open()=>inner.Open();public void StartEntryPulse()=>inner.StartEntryPulse();public void StopEntryPulse()=>inner.StopEntryPulse();
    public void ClearReceive()=>inner.ClearReceive();public void RequestIdentity()=>inner.RequestIdentity();
    public RecoveryFrame? Read(int timeout){try{return inner.Read(timeout);}catch{inner.FailOriginalRestore();throw;}}
    public void SendRestoreStep(int index,CancellationToken cancel=default)=>inner.SendOriginalRestoreStep(index,cancel);
    public void Dispose()=>inner.Dispose();
}
