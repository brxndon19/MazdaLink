using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Separate opt-in capability: exact boot binding followed by one byte-verified RAM watchdog/initialization rehearsal.</summary>
public sealed class NativeRecoveryRehearsalChannel : IRecoveryRehearsalChannel
{
    private readonly NativeBenchMonitorChannel inner;
    private readonly RecoveryRehearsalPlan plan;
    public NativeRecoveryRehearsalChannel(J2534Driver driver, RecoveryBindingPlan binding)
    { plan = new(binding); inner = new(driver, plan); }
    public string? CleanupWarning => inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics => inner.Diagnostics;
    public RamSelfTestTransportDiagnostics RehearsalDiagnostics
    {
        get { var d=inner.RamDiagnostics; return new(Math.Max(0,d.SentSteps-plan.Binding.Count),Math.Max(0,d.VerifiedBytes-RecoveryBindingPlan.PreservedBytes),d.SentSteps>plan.Binding.Count && d.AwaitingReply,d.Failed); }
    }
    public void Open() => inner.Open();
    public void StartEntryPulse() => inner.StartEntryPulse();
    public void StopEntryPulse() => inner.StopEntryPulse();
    public void ClearReceive() => inner.ClearReceive();
    public void RequestIdentity() => inner.RequestIdentity();
    public RecoveryFrame? Read(int timeoutMilliseconds)
    {
        try { return inner.Read(timeoutMilliseconds); }
        catch { inner.FailRamSelfTest(); throw; }
    }
    public RecoveryBindingTransportDiagnostics BindingDiagnostics
    {
        get { var d=inner.RamDiagnostics; bool binding=d.SentSteps<=plan.Binding.Count;
            return new(Math.Min(d.SentSteps,plan.Binding.Count),Math.Min(d.VerifiedBytes,RecoveryBindingPlan.PreservedBytes),binding && d.AwaitingReply,binding && d.Failed); }
    }
    public void SendBindingStep(int index)
    {
        if (index < 0 || index >= plan.Binding.Count) { inner.FailRamSelfTest(); throw new TransportException("Only fixed boot-binding indices are allowed."); }
        inner.SendRecoveryRehearsalStep(index);
    }
    public void SendRehearsalStep(int index)
    {
        if (index < 0 || index >= Zyj2RecoveryRehearsalPolicy.Steps.Count) { inner.FailRamSelfTest(); throw new TransportException("Only fixed control indices are allowed."); }
        inner.SendRecoveryRehearsalStep(plan.Binding.Count + index);
    }
    public void Dispose() => inner.Dispose();
}
