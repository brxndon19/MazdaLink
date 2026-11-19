using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Separate fixed preserved-ROM binding capability; no upload or execution interface.</summary>
public sealed class NativeRecoveryBindingChannel : IRecoveryBindingChannel
{
    private readonly NativeBenchMonitorChannel inner;
    public NativeRecoveryBindingChannel(J2534Driver driver, RecoveryBindingPlan plan) => inner = new(driver, plan);
    public string? CleanupWarning => inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics => inner.Diagnostics;
    public RecoveryBindingTransportDiagnostics BindingDiagnostics => inner.BindingDiagnostics;
    public void Open() => inner.Open();
    public void StartEntryPulse() => inner.StartEntryPulse();
    public void StopEntryPulse() => inner.StopEntryPulse();
    public void ClearReceive() => inner.ClearReceive();
    public void RequestIdentity() => inner.RequestIdentity();
    public RecoveryFrame? Read(int timeoutMilliseconds)
    {
        try { return inner.Read(timeoutMilliseconds); }
        catch { inner.FailRecoveryBinding(); throw; }
    }
    public void SendBindingStep(int index) => inner.SendRecoveryBindingStep(index);
    public void Dispose() => inner.Dispose();
}
