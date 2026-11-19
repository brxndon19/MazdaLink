using System.Buffers.Binary;
using System.Reflection;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class RecoveryBindingTests
{
    // Synthetic test bytes, never customer firmware. Only tests invoke the private snapshot constructor.
    internal static byte[] SyntheticBytes() => Enumerable.Range(0, RecoveryBindingPlan.PreservedBytes).Select(i => (byte)(i * 31 ^ 0xA5)).ToArray();
    internal static RecoveryBindingPlan SyntheticPlan(byte[]? bytes = null) => (RecoveryBindingPlan)Activator.CreateInstance(
        typeof(RecoveryBindingPlan), BindingFlags.Instance | BindingFlags.NonPublic, null, [bytes ?? SyntheticBytes()], null)!;

    [Fact]
    public void PublicBindingFactoryRejectsAnUnknownBackupProfile()
    {
        Assert.Throws<InvalidDataException>(() => RecoveryBindingPlan.Create(new(new byte[TargetPolicy.RomSize], Zyj2RecoveryPolicy.SoftwareId)));
        Assert.Throws<InvalidDataException>(() => RecoveryBindingPlan.Create(new(new byte[TargetPolicy.RomSize], "ZY87EC000ZYV5050")));
    }
    [Fact]
    public void EveryBindingWireStepIsRestrictedToItsFixedPreservedRangeOrFinalIdentity()
    {
        var expected = SyntheticBytes(); var plan = SyntheticPlan(expected);
        Assert.Equal(57857, plan.Count); Assert.Equal(28928, RecoveryBindingPlan.PreservedBytes);
        for (int i = 0; i < plan.Count - 1; i++)
        {
            var step = plan[i]; var p = step.Payload; int offset = i / 2; bool address = i % 2 == 0;
            Assert.Equal(address ? RamSelfTestOperation.Address : RamSelfTestOperation.VerifyByte, step.Operation);
            Assert.Equal(0x7A, p[0]); Assert.Equal(address ? 0x9C : 0xB4, p[1]);
            Assert.Equal((uint)(address ? offset : offset + 1), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2)));
            Assert.Equal(new byte[2], p[6..]); Assert.True(step.ExpectsReply);
            byte[] response = address ? p : [0x7A, 0xB1, expected[offset], 9, 8, 7, 6, 5];
            Assert.True(step.MatchesReply(new(0x21,0x100,response)));
        }
        Assert.Equal(RamSelfTestOperation.IdentifyMonitor, plan[^1].Operation);
        Assert.Equal(Zyj2RecoveryPolicy.IdentityRequest, plan[^1].Payload);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan[plan.Count]);
    }
    [Fact]
    public void BindingSnapshotAndReturnedPacketsCannotBeMutatedByTheCaller()
    {
        var bytes = SyntheticBytes(); byte first = bytes[0]; var plan = SyntheticPlan(bytes); string hash = plan.ExpectedBootSha256;
        bytes[0] ^= 1; plan[0].Payload[2] = 1;
        Assert.Equal(0, plan[0].Payload[2]); Assert.Equal(hash, plan.ExpectedBootSha256);
        Assert.True(plan[1].MatchesReply(new(0x21,0x100,[0x7A,0xB1,first,0,0,0,0,0])));
    }
    [Fact]
    public void BindingReadsEveryPreservedByteAndHashesObservedRepliesWithoutUploadOrExecution()
    {
        using var peer = new Peer(); var plan = SyntheticPlan(); var stages = new List<string>();
        var result = new RecoveryBindingCheck().Run(peer,plan,stages.Add);
        Assert.Equal(RecoveryBindingPlan.PreservedBytes,result.VerifiedBytes);
        Assert.Equal(plan.ExpectedBootSha256,result.ObservedBootSha256);
        Assert.Equal(57857,peer.Sent);
        Assert.Equal("monitor-return-confirmed-after-preserved-boot-binding",stages.Last());
        Assert.Equal(7,stages.Count(s=>s.StartsWith("preserved-boot-bytes-verified:")));
    }
    [Theory]
    [InlineData(0)] [InlineData(0xFFB)] [InlineData(0x6200)] [InlineData(0x7000)] [InlineData(0x70FF)]
    public void AnyPreservedByteMismatchStopsBeforeBindingCanPass(int offset)
    {
        using var peer = new Peer { Corrupt = offset }; RecoveryBindingResult? observed = null;
        Assert.Throws<TransportException>(() => new RecoveryBindingCheck().Run(peer,SyntheticPlan(),_=>{},observeResult:r=>observed=r));
        Assert.Null(observed); Assert.Equal(offset*2+2,peer.Sent);
    }
    [Fact]
    public void MatchedBootEvidenceSurvivesAFailedFinalMonitorReply()
    {
        using var peer = new Peer { BadIdentity = true }; RecoveryBindingResult? observed = null;
        var stages = new List<string>();
        Assert.Throws<TransportException>(() => new RecoveryBindingCheck().Run(peer,SyntheticPlan(),stages.Add,observeResult:r=>observed=r));
        Assert.NotNull(observed); Assert.Equal(28928,observed.VerifiedBytes);
        Assert.DoesNotContain("monitor-return-confirmed-after-preserved-boot-binding",stages);
    }
    [Fact]
    public void CancelledBindingStopsBeforeAnotherRequest()
    {
        using var cancel = new CancellationTokenSource(); using var peer = new Peer { OnSend = _=>cancel.Cancel() };
        Assert.Throws<OperationCanceledException>(() => new RecoveryBindingCheck().Run(peer,SyntheticPlan(),_=>{},cancel.Token));
        Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void BindingEnforcesAWholeRunDeadlineWithoutRetrying()
    {
        var clock = new Clock(); using var peer = new Peer { OnSend = _=>clock.Advance(181) };
        var error = Assert.Throws<TransportException>(()=>new RecoveryBindingCheck(clock).Run(peer,SyntheticPlan(),_=>{}));
        Assert.Contains("three-minute",error.Message); Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void BindingEnforcesTheSingleReplyDeadlineWithoutRetrying()
    {
        var clock = new Clock(); using var peer = new Peer { OnRead = ()=>clock.Advance(3), DropReply = true };
        var error = Assert.Throws<TransportException>(()=>new RecoveryBindingCheck(clock).Run(peer,SyntheticPlan(),_=>{}));
        Assert.Contains("response timed out",error.Message); Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void ExcessBindingEchoesCannotHideAnUnboundedReceiveLoop()
    {
        using var peer = new Peer { Echoes = 65 };
        Assert.Throws<TransportException>(()=>new RecoveryBindingCheck().Run(peer,SyntheticPlan(),_=>{}));
        Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void BindingCannotOpenHardwareForMissingBackupsOrExternalOverrides()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            recoveryBinding:_=>{opens++;throw new Exception();});
        string absent=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"original.rom");
        Assert.Equal(1,app.Execute(["recovery-binding-check",absent]));
        Assert.Equal(1,app.Execute(["recovery-binding-check",absent,"0xC0000"]));
        Assert.Equal(1,app.Execute(["recovery-binding-check",absent,"external.bin"]));
        Assert.Equal(0,opens);
    }
    private sealed class Clock:TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public void Advance(int seconds)=>ticks+=TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Peer:IRecoveryBindingChannel
    {
        private readonly byte[] bytes=SyntheticBytes(); private readonly Queue<RecoveryFrame> replies=new();
        public int Sent; public int Corrupt {get;init;}=-1;public bool BadIdentity {get;init;}public bool DropReply{get;init;}
        public int Echoes{get;init;}public Action<int>? OnSend{get;init;}public Action? OnRead{get;init;}
        public void SendBindingStep(int index)
        {
            Assert.Equal(Sent++,index); Assert.Empty(replies);OnSend?.Invoke(index);
            var step=SyntheticPlanCache[index];byte[] response;
            if(step.Operation==RamSelfTestOperation.Address)response=step.Payload;
            else if(step.Operation==RamSelfTestOperation.VerifyByte)
            {byte value=bytes[index/2];if(index/2==Corrupt)value^=1;response=[0x7A,0xB1,value,0,0,0,0,0];}
            else {Assert.Equal(RamSelfTestOperation.IdentifyMonitor,step.Operation);response=Zyj2RecoveryPolicy.ExpectedIdentity;if(BadIdentity)response[7]^=1;}
            for(int i=0;i<Echoes;i++)replies.Enqueue(new(0,0x101,[]));
            if(!DropReply)replies.Enqueue(new(0x21,0x100,response));
        }
        private static readonly RecoveryBindingPlan SyntheticPlanCache=SyntheticPlan();
        public RecoveryFrame? Read(int milliseconds){OnRead?.Invoke();return replies.TryDequeue(out var f)?f:null;}
        public void Open()=>throw new Exception("Entry is separate");public void StartEntryPulse()=>throw new Exception();
        public void StopEntryPulse()=>throw new Exception();public void ClearReceive()=>throw new Exception();public void RequestIdentity()=>throw new Exception();
        public void Dispose(){}public string? CleanupWarning=>null;
    }
}
