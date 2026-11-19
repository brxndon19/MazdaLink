using MazdaLink.Core;

namespace MazdaLink.J2534;

/// <summary>Separate opt-in capability for one built-in, byte-verified protected initialization test.</summary>
public sealed class NativeProtectedInitializationChannel : IProtectedInitializationChannel
{
    private readonly NativeBenchMonitorChannel inner;
    public NativeProtectedInitializationChannel(J2534Driver driver) => inner = new(driver, allowRamSelfTest: false, allowProtectedInitialization: true);
    public string? CleanupWarning => inner.CleanupWarning;
    public BenchChannelDiagnostics Diagnostics => inner.Diagnostics;
    public RamSelfTestTransportDiagnostics InitializationDiagnostics => inner.RamDiagnostics;
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
    public void SendInitializationStep(int index) => inner.SendProtectedInitializationStep(index);
    public void Dispose() => inner.Dispose();
}
