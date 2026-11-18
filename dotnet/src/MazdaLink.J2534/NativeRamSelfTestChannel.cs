using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Separate opt-in capability for one built-in, byte-verified volatile RAM test.</summary>
public sealed class NativeRamSelfTestChannel : IFixedRamSelfTestChannel
{
    private readonly NativeBenchMonitorChannel inner;
    public NativeRamSelfTestChannel(J2534Driver driver) => inner = new(driver, allowRamSelfTest: true);
    public string? CleanupWarning => inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics => inner.Diagnostics;
    public RamSelfTestTransportDiagnostics SelfTestDiagnostics => inner.RamDiagnostics;
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
    public void SendSelfTestStep(int index) => inner.SendFixedRamSelfTestStep(index);
    public void Dispose() => inner.Dispose();
}
