using System.Buffers.Binary;
using System.Diagnostics;

namespace MazdaLink.Core;

public interface IFixedRamSelfTestChannel : IBenchMonitorChannel
{
    // Index into the immutable built-in script. No caller-supplied address or code.
    void SendSelfTestStep(int index);
    RamSelfTestTransportDiagnostics? SelfTestDiagnostics => null;
}

public sealed record RamSelfTestTransportDiagnostics(int SentSteps, int VerifiedBytes, bool AwaitingReply, bool Failed);

public enum RamSelfTestOperation { Address, Upload, VerifyByte, ExecuteFixedTest, IdentifyMonitor }

public sealed class RamSelfTestStep
{
    private readonly byte[] payload, reply;
    public RamSelfTestOperation Operation { get; }
    public int ImageOffset { get; }
    public byte[] Payload => payload.ToArray();
    public bool ExpectsReply => reply.Length != 0;
    internal RamSelfTestStep(RamSelfTestOperation operation, byte[] payload, byte[] reply, int imageOffset = -1)
    { Operation = operation; this.payload = payload; this.reply = reply; ImageOffset = imageOffset; }
    public bool MatchesReply(RecoveryFrame frame) => frame.Id == Zyj2RecoveryPolicy.ResponseId &&
        frame.Status == 0x100 && frame.Data.Length == 8 && reply.Length > 0 &&
        frame.Data.AsSpan(0, reply.Length).SequenceEqual(reply);
}

/// <summary>Fixed volatile RAM test only. No flash algorithm or external image can be uploaded.</summary>
public static class Zyj2RamSelfTestPolicy
{
    public const uint Address = 0xFFFF8000;
    public const string ImageSha256 = "311ce48738ec5699ac0980c82e68acee4aaaaf61664755cda4d2183896b8ebb0";
    private static readonly byte[] image = Convert.FromHexString(
        "D607D10860108064710560108065E4C0E5069309430B000994079307432B0009" +
        "FFFF8030FFFFE80004C20924004000094D4C523100000000");
    private static readonly IReadOnlyList<RamSelfTestStep> steps = Build().AsReadOnly();
    public static byte[] Image => image.ToArray();
    public static IReadOnlyList<RamSelfTestStep> Steps => steps;
    private static List<RamSelfTestStep> Build()
    {
        if (image.Length != 56 || RomStorage.Hash(image) != ImageSha256) throw new InvalidDataException("RAM self-test image integrity failed.");
        var result = new List<RamSelfTestStep>();
        void SetAddress(uint value)
        {
            byte[] request = [0x7A, 0x9C, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(2), value);
            // The resident sender leaves unused trailing bytes unchanged.
            result.Add(new(RamSelfTestOperation.Address, request, request.AsSpan(0, 6).ToArray()));
        }
        SetAddress(Address);
        for (int offset = 0; offset < image.Length; offset += 6)
        {
            int length = Math.Min(6, image.Length - offset);
            byte[] packet = new byte[8]; packet[0] = 0x7A; packet[1] = (byte)(0xA8 + length);
            image.AsSpan(offset, length).CopyTo(packet.AsSpan(2));
            // Successful copies have no ECU acknowledgement. Verify every byte below.
            result.Add(new(RamSelfTestOperation.Upload, packet, [], offset));
        }
        for (int offset = 0; offset < image.Length; offset++)
        {
            SetAddress(Address + (uint)offset);
            byte[] request = [0x7A, 0xB4, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(2), Address + (uint)offset + 1);
            // Native end-around-carry checksum of one byte is that byte exactly.
            // This avoids accepting the weak checksum of the whole executable.
            result.Add(new(RamSelfTestOperation.VerifyByte, request, [0x7A, 0xB1, image[offset]], offset));
        }
        SetAddress(Address);
        result.Add(new(RamSelfTestOperation.ExecuteFixedTest, [0x7A, 0xA0, 0, 0, 0, 0, 0, 0], [0x7A, 0xC6, 0x4D, 0x4C, 0x52, 0x31]));
        result.Add(new(RamSelfTestOperation.IdentifyMonitor, Zyj2RecoveryPolicy.IdentityRequest, Zyj2RecoveryPolicy.ExpectedIdentity));
        return result;
    }
}

public sealed record RamSelfTestResult(int UploadedBytes, int VerifiedBytes, byte Fccs, byte MatSelection)
{
    public bool FlashWriteEnableObserved => (Fccs & 0x80) != 0;
    public bool FlashErrorObserved => (Fccs & 0x10) != 0;
    public bool UserBootMatSelected => MatSelection == 0xAA;
    // These observations never authorize a flash operation.
}

public sealed class Zyj2RamSelfTest
{
    public RamSelfTestResult Run(IFixedRamSelfTestChannel channel, Action<string> record, CancellationToken cancel = default)
    {
        int uploaded = 0, verified = 0; byte? fccs = null, mat = null;
        record("fixed-ram-self-test-started-no-flash");
        for (int index = 0; index < Zyj2RamSelfTestPolicy.Steps.Count; index++)
        {
            cancel.ThrowIfCancellationRequested();
            var step = Zyj2RamSelfTestPolicy.Steps[index];
            channel.SendSelfTestStep(index);
            if (step.Operation == RamSelfTestOperation.Upload)
            {
                uploaded += step.Payload[1] & 7;
                if (uploaded == Zyj2RamSelfTestPolicy.Image.Length) record("fixed-ram-image-uploaded-not-executed");
            }
            if (!step.ExpectsReply) continue;
            var frame = Receive(channel, step, cancel);
            if (step.Operation == RamSelfTestOperation.VerifyByte)
            {
                verified++;
                if (verified == Zyj2RamSelfTestPolicy.Image.Length) record("every-fixed-ram-image-byte-verified");
            }
            if (step.Operation == RamSelfTestOperation.ExecuteFixedTest)
            {
                fccs = frame.Data[6]; mat = frame.Data[7];
                record("fixed-ram-marker-received-status-registers-read-only");
            }
        }
        if (uploaded != 56 || verified != 56 || fccs is null || mat is null)
            throw new TransportException("RAM self-test evidence is incomplete. No flash operation was sent.");
        record("monitor-return-confirmed-after-fixed-ram-self-test");
        return new(uploaded, verified, fccs.Value, mat.Value);
    }
    private static RecoveryFrame Receive(IFixedRamSelfTestChannel channel, RamSelfTestStep step, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew(); int count = 0;
        while (watch.ElapsedMilliseconds < 2000)
        {
            cancel.ThrowIfCancellationRequested();
            var frame = channel.Read(25);
            if (frame is null) continue;
            if (++count > 64) throw new TransportException("Too many RAM self-test frames. Stop and power-cycle the spare.");
            if ((frame.Status & 0x0B) != 0) continue;
            if (!step.MatchesReply(frame)) throw new TransportException("RAM self-test reply or byte readback mismatch. Execution stopped; no command was retried.");
            return frame;
        }
        throw new TransportException("RAM self-test response timed out. Execution stopped; no command was retried. Power-cycle the spare.");
    }
}
