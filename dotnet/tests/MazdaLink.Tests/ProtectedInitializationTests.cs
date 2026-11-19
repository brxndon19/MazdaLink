using MazdaLink.Core;
using System.Buffers.Binary;

namespace MazdaLink.Tests;

public sealed class ProtectedInitializationTests
{
    [Fact]
    public void FixedInitializationProgramAndScriptRemainImmutable()
    {
        byte[] image = Zyj2ProtectedInitializationPolicy.Image; image[0] ^= 1;
        byte[] payload = Zyj2ProtectedInitializationPolicy.Steps[0].Payload; payload[2] = 0;
        Assert.Equal(Zyj2ProtectedInitializationPolicy.ImageSha256, RomStorage.Hash(Zyj2ProtectedInitializationPolicy.Image));
        Assert.Equal(0xFF, Zyj2ProtectedInitializationPolicy.Steps[0].Payload[2]);
        Assert.Equal(936, Zyj2ProtectedInitializationPolicy.Image.Length);
    }
    [Fact]
    public void InitializationUploadsOnlyTheFixedImageAndVerifiesEveryByteBeforeExecution()
    {
        using var peer = new Peer(); var stages = new List<string>();
        var result = new Zyj2ProtectedInitialization().Run(peer, stages.Add);
        Assert.Equal(Zyj2ProtectedInitializationPolicy.Image, peer.Memory);
        Assert.Equal(936, result.UploadedBytes); Assert.Equal(936, result.VerifiedBytes);
        Assert.Equal(Enumerable.Range(0, 936), peer.Verified);
        Assert.Equal(1, peer.Executions); Assert.True(result.ProtectionChecksPassed);
        Assert.Equal("monitor-return-confirmed-after-protected-initialization", stages.Last());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UploadEchoBurstIsDrainedWithoutClearingAnEcuUploadError(bool error)
    {
        using var peer = new Peer { UploadEchoes = true, UploadError = error };
        if (error)
        {
            Assert.Throws<TransportException>(() => new Zyj2ProtectedInitialization().Run(peer, _ => { }));
            Assert.Equal(0, peer.Executions);
        }
        else Assert.True(new Zyj2ProtectedInitialization().Run(peer, _ => { }).ProtectionChecksPassed);
    }
    [Theory]
    [InlineData(0)] [InlineData(417)] [InlineData(935)]
    public void AnyInitializationImageByteMismatchPreventsExecution(int offset)
    {
        using var peer = new Peer { Corrupt = offset };
        Assert.Throws<TransportException>(() => new Zyj2ProtectedInitialization().Run(peer, _ => { }));
        Assert.Equal(0, peer.Executions);
    }
    [Theory]
    [InlineData("protected-init-image-uploaded-not-executed")]
    [InlineData("every-protected-init-image-byte-verified")]
    public void InitializationCancellationBeforeExecuteNeverRunsTheControllerProgram(string stop)
    {
        using var peer = new Peer(); using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new Zyj2ProtectedInitialization().Run(peer,
            stage => { if (stage == stop) cancel.Cancel(); }, cancel.Token));
        Assert.Equal(0, peer.Executions);
    }
    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public void WrongInitializationMarkerOrMonitorReturnCannotPass(bool marker, bool monitor)
    {
        using var peer = new Peer { BadMarker = marker, BadMonitor = monitor };
        Assert.Throws<TransportException>(() => new Zyj2ProtectedInitialization().Run(peer, _ => { }));
    }
    [Fact]
    public void InitializationResultRemainsObservableIfMonitorReturnFails()
    {
        using var peer = new Peer { BadMonitor = true };
        ProtectedInitializationResult? observed = null; var stages = new List<string>();
        Assert.Throws<TransportException>(() => new Zyj2ProtectedInitialization().Run(peer, stages.Add,
            observeResult: result => observed = result));
        Assert.NotNull(observed); Assert.Equal(936, observed.VerifiedBytes);
        Assert.True(observed.ProtectionChecksPassed);
        Assert.DoesNotContain("monitor-return-confirmed-after-protected-initialization", stages);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(255)]
    public void NonzeroInitializationStatusCannotQualifyAControllerTest(int status)
    {
        using var peer = new Peer { Status = (byte)status };
        var result = new Zyj2ProtectedInitialization().Run(peer, _ => { });
        Assert.False(result.ProtectionChecksPassed); Assert.Equal(status, result.Status);
    }
    [Theory]
    [InlineData(1,0,0)] [InlineData(0,1,0)] [InlineData(0,0,1)]
    public void ZeroStatusCannotHideFailedInitializationResultBytes(int program, int erase, int init)
    {
        Assert.False(new ProtectedInitializationResult(936,936,0,(byte)program,(byte)erase,(byte)init).ProtectionChecksPassed);
    }
    [Fact]
    public void MissingBackupsAndExternalCodeOverridesCannotOpenTheInitializationBackend()
    {
        int opens = 0;
        var app = new Terminal(() => { opens++; throw new Exception(); }, new StringReader(""), new StringWriter(),
            Path.GetTempPath(), protectedInitialization: () => { opens++; throw new Exception(); });
        string absent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "original.rom");
        Assert.Equal(1, app.Execute(["protected-flash-init", absent]));
        Assert.Equal(1, app.Execute(["protected-flash-init", absent, "external.bin"]));
        Assert.Equal(1, app.Execute(["protected-flash-init", absent, "0xFFFF1000"]));
        Assert.Equal(1, app.Execute(["flash"])); Assert.Equal(0, opens);
    }
    private sealed class Peer : IProtectedInitializationChannel
    {
        public byte[] Memory { get; } = new byte[936];
        public List<int> Verified { get; } = [];
        public int Executions;
        public int Corrupt { get; init; } = -1;
        public bool BadMarker { get; init; }
        public bool BadMonitor { get; init; }
        public byte Status { get; init; }
        public bool UploadEchoes { get; init; }
        public bool UploadError { get; init; }
        private int next; private uint address;
        private readonly Queue<RecoveryFrame> replies = new();
        public void SendInitializationStep(int index)
        {
            Assert.Equal(next++, index);
            var step = Zyj2ProtectedInitializationPolicy.Steps[index]; byte[] p = step.Payload;
            void Reply(byte[] bytes) => replies.Enqueue(new(0x21, 0x100, bytes));
            switch (step.Operation)
            {
                case RamSelfTestOperation.Address:
                    address = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2));
                    Assert.InRange(address, 0xFFFF8000u, 0xFFFF83A7u); Reply(p); break;
                case RamSelfTestOperation.Upload:
                    int count = p[1] & 7;
                    p.AsSpan(2, count).CopyTo(Memory.AsSpan((int)(address - 0xFFFF8000))); address += (uint)count;
                    if (UploadEchoes) replies.Enqueue(new(0, 0x101, []));
                    if (UploadError && step.ImageOffset == 6) Reply([0x7A,0xA1,2,0,0,0,0,0]);
                    break;
                case RamSelfTestOperation.VerifyByte:
                    int offset = (int)(address - 0xFFFF8000);
                    Assert.Equal(address + 1, BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2)));
                    Verified.Add(offset); byte value = Memory[offset]; if (offset == Corrupt) value ^= 1;
                    Reply([0x7A,0xB1,value,9,8,7,6,5]); break;
                case RamSelfTestOperation.ExecuteFixedTest:
                    Assert.Equal(Enumerable.Range(0,936),Verified);
                    Assert.Equal(0xFFFF8000u,address);
                    Assert.Equal(Zyj2ProtectedInitializationPolicy.Image,Memory); Executions++;
                    Reply([0x7A,0xC6,BadMarker ? (byte)0 : (byte)0x4D,0x49,Status,0,0,0]); break;
                case RamSelfTestOperation.IdentifyMonitor:
                    var identity = Zyj2RecoveryPolicy.ExpectedIdentity; if (BadMonitor) identity[7] ^= 1; Reply(identity); break;
            }
        }
        public RecoveryFrame? Read(int timeoutMilliseconds) => replies.TryDequeue(out var r) ? r : null;
        public void Dispose() { }
        public void Open() => throw new Exception("Separate entry is required");
        public void StartEntryPulse() => throw new Exception();
        public void StopEntryPulse() => throw new Exception();
        public void ClearReceive() => throw new Exception();
        public void RequestIdentity() => throw new Exception();
        public string? CleanupWarning => null;
    }
}
