namespace MazdaLink.Core;

// Explicit metadata-only schemas: never add frame payloads, memory, keys or VINs.
public sealed record BenchReceiveDiagnostics(int Calls, int EmptyReads, int ReturnedFrames,
    int TxEchoes, int Notifications, int ExpectedIdFrames, uint LastDriverStatus,
    uint LastMessageCount, uint LastProtocol, uint LastRxStatus, uint LastDataSize,
    uint LastExtraDataIndex, uint? LastCanId, bool? LastPayloadMatched, double MaxCallMilliseconds);

public sealed record BenchSupplyReading(uint DriverStatus, int? Millivolts);
public sealed record BenchConfigurationReading(uint DriverStatus, uint? Value);
public sealed record BenchTrafficBin(int Second, int TxEchoes, int OtherReturnedFrames);

public sealed record BenchChannelDiagnostics(string? Adapter, string? FirmwareVersion,
    string? DriverVersion, string? ApiVersion, uint Bitrate, uint RequestId, uint ResponseId,
    uint? LoopbackConfigurationStatus, BenchConfigurationReading? LoopbackReadback, BenchSupplyReading? SupplyBeforeEntry,
    BenchSupplyReading? SupplyAfterEntry, int StartCalls, int StopCalls, int IdentityRequests,
    double StartCallMilliseconds, double StopCallMilliseconds, double IdentityCallMilliseconds, double? EntryActiveMilliseconds,
    BenchReceiveDiagnostics EntryReceive, BenchReceiveDiagnostics IdentityReceive,
    IReadOnlyList<BenchTrafficBin> EntryTraffic);

/// <summary>Uses measured monotonic time, so rounded/slow polling does not accumulate drift.</summary>
public static class BenchEntryWindow
{
    public static void Wait(TimeSpan duration, Action<int> poll, CancellationToken cancel = default,
        TimeProvider? clock = null, long? startedTimestamp = null)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        clock ??= TimeProvider.System;
        long started = startedTimestamp ?? clock.GetTimestamp();
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            TimeSpan remaining = duration - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return;
            poll((int)Math.Clamp(Math.Ceiling(remaining.TotalMilliseconds), 1, 25));
        }
    }
}
