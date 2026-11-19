using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MazdaLink.Core;

public interface IRecoveryBindingChannel : IBenchMonitorChannel
{
    void SendBindingStep(int index);
    RecoveryBindingTransportDiagnostics? BindingDiagnostics => null;
}

public sealed record RecoveryBindingTransportDiagnostics(int SentSteps, int VerifiedBytes, bool AwaitingReply, bool Failed);

/// <summary>Only fixed preserved-ROM reads and one final monitor identity. No upload or execution.</summary>
public sealed class RecoveryBindingPlan : IReadOnlyList<RamSelfTestStep>
{
    public const int PreservedBytes = 0x7100;
    private readonly byte[] expected;
    public string ExpectedBootSha256 { get; }
    private RecoveryBindingPlan(byte[] preserved)
    {
        if (preserved.Length != PreservedBytes) throw new InvalidDataException("Incorrect preserved-region length.");
        expected = preserved.ToArray();
        ExpectedBootSha256 = RomStorage.Hash(expected);
    }
    public static RecoveryBindingPlan Create(VerifiedRawBackup backup)
    {
        Zyj2RecoveryPolicy.RequireBackup(backup);
        return new(backup.Data.AsSpan(0, PreservedBytes).ToArray());
    }
    public int Count => PreservedBytes * 2 + 1;
    public RamSelfTestStep this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == Count - 1)
                return new(RamSelfTestOperation.IdentifyMonitor, Zyj2RecoveryPolicy.IdentityRequest, Zyj2RecoveryPolicy.ExpectedIdentity);
            int offset = index / 2;
            bool address = index % 2 == 0;
            byte[] packet = [0x7A, address ? (byte)0x9C : (byte)0xB4, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(2), (uint)(address ? offset : offset + 1));
            return address
                ? new(RamSelfTestOperation.Address, packet, packet.AsSpan(0, 6).ToArray())
                : new(RamSelfTestOperation.VerifyByte, packet, [0x7A, 0xB1, expected[offset]], offset);
        }
    }
    public IEnumerator<RamSelfTestStep> GetEnumerator()
    {
        for (int index = 0; index < Count; index++) yield return this[index];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record RecoveryBindingResult(int VerifiedBytes, string ExpectedBootSha256, string ObservedBootSha256);

public sealed class RecoveryBindingCheck(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public RecoveryBindingResult Run(IRecoveryBindingChannel channel, RecoveryBindingPlan plan, Action<string> record,
        CancellationToken cancel = default, Action<RecoveryBindingResult>? observeResult = null)
    {
        long started = clock.GetTimestamp();
        void CheckBudget()
        {
            cancel.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) >= TimeSpan.FromMinutes(3))
                throw new TransportException("Preserved-boot binding exceeded its three-minute host budget. No request was retried.");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int verified = 0; RecoveryBindingResult? result = null;
        record("preserved-boot-binding-started-no-upload-or-execution");
        for (int index = 0; index < plan.Count; index++)
        {
            CheckBudget();
            var step = plan[index]; channel.SendBindingStep(index);
            var frame = Receive(channel, step, CheckBudget);
            if (step.Operation != RamSelfTestOperation.VerifyByte) continue;
            hash.AppendData(frame.Data.AsSpan(2, 1)); verified++;
            if (verified % 4096 == 0) record("preserved-boot-bytes-verified: " + verified);
            if (verified == RecoveryBindingPlan.PreservedBytes)
            {
                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (actual != plan.ExpectedBootSha256) throw new TransportException("Preserved-region hash did not match the backup.");
                result = new(verified, plan.ExpectedBootSha256, actual);
                observeResult?.Invoke(result);
                record("preserved-boot-region-matched-exactly-no-calibration-id-read");
            }
        }
        if (result is null || verified != RecoveryBindingPlan.PreservedBytes)
            throw new TransportException("Preserved-boot binding evidence is incomplete.");
        record("monitor-return-confirmed-after-preserved-boot-binding");
        return result;
    }
    private RecoveryFrame Receive(IRecoveryBindingChannel channel, RamSelfTestStep step, Action checkBudget)
    {
        long started = clock.GetTimestamp(); int frames = 0;
        while (clock.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
        {
            checkBudget();
            var frame = channel.Read(25);
            if (frame is null) { Thread.Sleep(1); continue; }
            if (++frames > 64) throw new TransportException("Too many preserved-boot binding frames. No request was retried.");
            if ((frame.Status & 0x0B) != 0) continue;
            if (!step.MatchesReply(frame))
                throw new TransportException("Preserved-boot byte or monitor reply did not match the selected backup. Binding stopped; no request was retried.");
            return frame;
        }
        throw new TransportException("Preserved-boot binding response timed out. No request was retried.");
    }
}
