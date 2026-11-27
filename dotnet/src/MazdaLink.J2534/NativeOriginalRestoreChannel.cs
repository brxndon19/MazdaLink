using MazdaLink.Core;
namespace MazdaLink.J2534;

/// <summary>Separate original-only detached-spare qualification capability.</summary>
public sealed class NativeOriginalRestoreChannel:IOriginalRestoreChannel
{
    private readonly NativeBenchMonitorChannel inner;
    public NativeOriginalRestoreChannel(J2534Driver driver,RestoreQualificationPlan plan,RestoreJournal journal)
    {
        if(plan.Modified is not null)throw new InvalidDataException("Original-only capability cannot accept a modified transaction.");
        if(plan.Original.OriginalSha256!=journal.OriginalSha256||plan.SelectedBlock!=journal.SelectedBlock)throw new InvalidDataException("Restore journal is bound to another backup or erase block.");
        inner=new(driver,new RestoreQualificationGate(plan,journal));
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
