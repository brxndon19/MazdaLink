using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace MazdaLink.Core;

// Cleanup uncertainty ends the process instead of returning to the interactive menu.
public sealed class BenchCleanupException(string message) : Exception(message);

/// <summary>Identification-only bench experiment. This is not a flash or recovery implementation.</summary>
public static class Zyj2RecoveryPolicy
{
    public const string SoftwareId = "ZYJ2EA000ZYV5050";
    public const uint RequestId = 0x000FFFFE, ResponseId = 0x21;
    public const int Bitrate = 500000, PeriodMilliseconds = 5, EntryWindowMilliseconds = 20000;
    public static byte[] EntryRequest => [0xFF, 0x86, 0, 0, 0, 0, 0, 0];
    public static byte[] IdentityRequest => [0x7A, 0xC0, 0, 0, 0, 0, 0, 0];
    public static byte[] ExpectedIdentity => [0x7A, 0xC6, 0x50, 0x79, 0x77, 0x30, 0x58, 0x41];

    public static void RequireBackup(VerifiedRawBackup backup)
    {
        var rom = backup.Data;
        if (backup.ReportedCalibration != SoftwareId || rom.Length != TargetPolicy.RomSize ||
            !rom.AsSpan(0xC0000, 8).SequenceEqual("ZYJ2EA0W"u8) ||
            !rom.AsSpan(0xC0091, SoftwareId.Length).SequenceEqual(Encoding.ASCII.GetBytes(SoftwareId)) ||
            !rom.AsSpan(0x6200, 4).SequenceEqual("ZYC3"u8))
            throw new InvalidDataException("The bench check requires a complete, verified ZYJ2EA000ZYV5050 / ZYC3 MazdaLink backup.");
        // Code fingerprints exclude the variant/individual response data at 0xFF8..0xFFF
        // and identification fields at 0x6000 onward. Never distribute firmware fixtures.
        if (RomStorage.Hash(rom.AsSpan(0, 0xFF8)) != "1cd95680cc91843a2b2a0a46488cbf948b880a64ff4ea111cb019eb7a72a3755" ||
            RomStorage.Hash(rom.AsSpan(0x1000, 0x5000)) != "3e74b1ca85f541ea917e2c93d1bb58185351bce90e98e9a16ff73c6c1b87c662")
            throw new InvalidDataException("This backup does not match the independently examined ZYJ2 monitor and resident loader. No bench channel was opened.");
    }

    public static void RequireLiveTarget(IReadTransport link)
    {
        if (TargetPolicy.Identify(link) != SoftwareId)
            throw new InvalidDataException("The connected ECU must report ZYJ2EA000ZYV5050. Bench entry was not attempted.");
        byte[] rpm = link.Exchange([1, 0x0C]);
        if (rpm.Length != 4 || rpm[0] != 0x41 || rpm[1] != 0x0C || BinaryPrimitives.ReadUInt16BigEndian(rpm.AsSpan(2)) != 0)
            throw new TransportException("Engine RPM must be confirmed as zero before the bench check.");
        if (TargetPolicy.Identify(link) != SoftwareId)
            throw new InvalidDataException("The connected ECU identity changed. Bench entry was not attempted.");
    }

    public static void CheckTransmit(ReadOnlySpan<byte> payload, bool periodic)
    {
        if (!(periodic ? payload.SequenceEqual(EntryRequest) : payload.SequenceEqual(IdentityRequest)))
            throw new TransportException("Bench traffic blocked: only the fixed entry pulse and monitor identification request are implemented.");
    }

    public static void RequireMonitorIdentity(uint id, uint status, ReadOnlySpan<byte> data)
    {
        // CAN_29BIT_ID must be set, and no echo/notification/error status is accepted.
        if (id != ResponseId || status != 0x100 ||
            !data.SequenceEqual(ExpectedIdentity))
            throw new TransportException("The bench reply is not the expected extended-CAN ZYJ2 monitor identification.");
    }
}

public sealed record RecoveryFrame(uint Id, uint Status, byte[] Data);

/// <summary>No generic send, memory address, upload, erase, reset or execute method exists.</summary>
public interface IBenchMonitorChannel : IDisposable
{
    void Open();
    void StartEntryPulse();
    void StopEntryPulse();
    void ClearReceive();
    void RequestIdentity();
    RecoveryFrame? Read(int timeoutMilliseconds);
    string? CleanupWarning { get; }
    BenchChannelDiagnostics? Diagnostics => null;
}

public sealed class Zyj2BenchRecovery
{
    public void IdentifyMonitor(IBenchMonitorChannel channel, Action<string> record,
        Action<int, CancellationToken>? wait = null, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        channel.Open();
        record("bench-channel-opened");
        // Persist the intent before arming. No disk/UI callback runs while periodic
        // traffic is active, so slow evidence storage cannot extend the entry window.
        record("entry-window-armed");
        cancel.ThrowIfCancellationRequested();
        bool pulseAttempted = false;
        try
        {
            // Stop is attempted even if the driver reports a partial start failure.
            pulseAttempted = true;
            long started = TimeProvider.System.GetTimestamp();
            channel.StartEntryPulse();
            if (wait is not null) wait(Zyj2RecoveryPolicy.EntryWindowMilliseconds, cancel);
            else BenchEntryWindow.Wait(TimeSpan.FromMilliseconds(Zyj2RecoveryPolicy.EntryWindowMilliseconds), timeout =>
            {
                // Drain adapter notifications while entry is active. A frame here
                // never establishes monitor identity; that requires the later query.
                if (channel.Read(timeout) is null) Thread.Sleep(1);
            }, cancel, startedTimestamp: started);
            cancel.ThrowIfCancellationRequested();
        }
        finally
        {
            if (pulseAttempted) channel.StopEntryPulse();
        }
        record("entry-window-stopped");
        cancel.ThrowIfCancellationRequested();
        channel.ClearReceive();
        channel.RequestIdentity();
        var watch = Stopwatch.StartNew();
        int messages = 0;
        while (watch.ElapsedMilliseconds < 2000)
        {
            cancel.ThrowIfCancellationRequested();
            var frame = channel.Read(25);
            if (frame is null) continue;
            if (++messages > 64) throw new TransportException("Too many bench replies; the check was stopped.");
            if ((frame.Status & 0x0B) != 0) continue;
            if (frame.Id != Zyj2RecoveryPolicy.ResponseId) continue;
            Zyj2RecoveryPolicy.RequireMonitorIdentity(frame.Id, frame.Status, frame.Data);
            record("monitor-identified-no-flash-operation");
            return;
        }
        throw new TransportException("No valid monitor identification within two seconds. No request was resent. Power-cycle the spare before reconnecting normally.");
    }
}
