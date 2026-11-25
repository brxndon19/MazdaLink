using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Separate opt-in capability for one built-in, byte-verified read/staging transaction trial.</summary>
public sealed class NativeTransactionTrialChannel : ITransactionTrialChannel
{
    private readonly NativeBenchMonitorChannel inner;
    public NativeTransactionTrialChannel(J2534Driver driver,TransactionTrialPlan plan) => inner=new(driver,plan);
    public string? CleanupWarning => inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics => inner.Diagnostics;
    public TransactionTrialDiagnostics? TrialDiagnostics => inner.TrialDiagnostics;
    public void Open() => inner.Open();
    public void StartEntryPulse() => inner.StartEntryPulse();
    public void StopEntryPulse() => inner.StopEntryPulse();
    public void ClearReceive() => inner.ClearReceive();
    public void RequestIdentity() => inner.RequestIdentity();
    public RecoveryFrame? Read(int timeoutMilliseconds)
    {
        try { return inner.Read(timeoutMilliseconds); }
        catch { inner.FailTransactionTrial(); throw; }
    }
    public void SendTrialStep(int index) => inner.SendTransactionTrialStep(index);
    public void Dispose() => inner.Dispose();
}
