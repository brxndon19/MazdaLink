using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class RecoveryRehearsalTests
{
    [Fact]
    public void RehearsalPlanBindsPreservedBootBeforeOnlyItsImmutableFixedImage()
    {
        var binding=RecoveryBindingTests.SyntheticPlan();var plan=new RecoveryRehearsalPlan(binding);
        Assert.Equal(1404,Zyj2RecoveryRehearsalPolicy.Image.Length);
        Assert.Equal(Zyj2RecoveryRehearsalPolicy.ImageSha256,RomStorage.Hash(Zyj2RecoveryRehearsalPolicy.Image));
        Assert.Equal(60903,plan.Count);
        Assert.Equal(RamSelfTestOperation.IdentifyMonitor,plan[binding.Count-1].Operation);
        Assert.Equal(RamSelfTestOperation.Address,plan[binding.Count].Operation);
        Assert.Equal(1,plan.Count(s=>s.Operation==RamSelfTestOperation.ExecuteFixedTest));
        var image=Zyj2RecoveryRehearsalPolicy.Image;image[0]^=1;
        Assert.NotEqual(image,Zyj2RecoveryRehearsalPolicy.Image);
        var first=Zyj2RecoveryRehearsalPolicy.Steps[0].Payload;first[2]^=1;
        Assert.NotEqual(first,Zyj2RecoveryRehearsalPolicy.Steps[0].Payload);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan[plan.Count]);
    }
    [Fact]
    public void RehearsalRunnerVerifiesEveryByteAndRecordsResultBeforeMonitorReturn()
    {
        using var peer=new Peer();var stages=new List<string>();
        var result=new Zyj2RecoveryRehearsal().Run(peer,stages.Add);
        Assert.True(result.Passed);
        Assert.Equal(1404,result.UploadedBytes);Assert.Equal(1404,result.VerifiedBytes);
        Assert.Equal(1,peer.Executions);Assert.Equal(3046,peer.Sent);
        Assert.Equal("monitor-return-confirmed-after-recovery-rehearsal",stages.Last());
    }
    [Theory]
    [InlineData(0)] [InlineData(702)] [InlineData(1403)]
    public void AnyRehearsalByteMismatchPreventsExecution(int bad)
    {
        using var peer=new Peer{BadByte=bad};
        Assert.Throws<TransportException>(()=>new Zyj2RecoveryRehearsal().Run(peer,_=>{}));
        Assert.Equal(0,peer.Executions);
    }
    [Fact]
    public void CancelledRehearsalCannotAdvanceToExecution()
    {
        using var cancel=new CancellationTokenSource();using var peer=new Peer{OnSend=()=>cancel.Cancel()};
        Assert.Throws<OperationCanceledException>(()=>new Zyj2RecoveryRehearsal().Run(peer,_=>{},cancel.Token));
        Assert.Equal(0,peer.Executions);Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void RehearsalResultRemainsObservableIfFinalMonitorFails()
    {
        using var peer=new Peer{BadMonitor=true};RecoveryRehearsalResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2RecoveryRehearsal().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.NotNull(result);Assert.True(result.Passed);
    }
    [Fact]
    public void FailedRehearsalRetainsResultAndStopsBeforeAnyMonitorQuery()
    {
        using var peer=new Peer{FailedStatus=true};RecoveryRehearsalResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2RecoveryRehearsal().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.NotNull(result);Assert.Equal(16,result.Status);
        Assert.Equal(Zyj2RecoveryRehearsalPolicy.Steps.Count-1,peer.Sent);
    }
    [Fact]
    public void WrongRehearsalResultMarkerCannotBecomeEvidence()
    {
        using var peer=new Peer{BadMarker=true};RecoveryRehearsalResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2RecoveryRehearsal().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.Null(result);
    }
    [Fact]
    public void FailedStatusOrIncompleteServiceEvidenceCannotQualifyRehearsal()
    {
        for(byte status=1;status<=16;status++)Assert.False(new RecoveryRehearsalResult(1404,1404,status,0,32,31).Passed);
        foreach(var bad in new[]{new RecoveryRehearsalResult(1404,1404,0,1,32,31),new(1404,1404,0,0,7,31),new(1404,1404,0,0,65,31),new(1404,1404,0,0,32,15),new(0,1404,0,0,32,31)})Assert.False(bad.Passed);
    }
    [Fact]
    public void MissingBackupsAndExternalOverridesCannotOpenRehearsalHardware()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            recoveryRehearsal:_=>{opens++;throw new Exception();});
        string missing=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"original.rom");
        Assert.Equal(1,app.Execute(["recovery-rehearsal",missing]));
        Assert.Equal(1,app.Execute(["recovery-rehearsal",missing,"external.bin"]));
        Assert.Equal(1,app.Execute(["recovery-rehearsal",missing,"0xfffff740"]));
        Assert.Equal(0,opens);
    }
    private sealed class Peer:IRecoveryRehearsalChannel
    {
        public int Sent,Executions; public int BadByte{get;init;}=-1;
        public bool FailedStatus{get;init;}public bool BadMonitor{get;init;}public bool BadMarker{get;init;}public Action? OnSend{get;init;}
        private RecoveryFrame? reply;
        public void SendRehearsalStep(int index)
        {
            Assert.Equal(Sent++,index);Assert.Null(reply);OnSend?.Invoke();
            var step=Zyj2RecoveryRehearsalPolicy.Steps[index];
            if(!step.ExpectsReply)return;
            byte[] data=step.Operation switch
            {
                RamSelfTestOperation.Address=>step.Payload,
                RamSelfTestOperation.VerifyByte=>[0x7A,0xB1,(byte)(Zyj2RecoveryRehearsalPolicy.Image[step.ImageOffset]^(step.ImageOffset==BadByte?1:0)),0,0,0,0,0],
                RamSelfTestOperation.ExecuteFixedTest=>[0x7A,0xC6,0x4D,BadMarker?(byte)0x49:(byte)0x52,FailedStatus?(byte)16:(byte)0,0,32,31],
                RamSelfTestOperation.IdentifyMonitor=>Zyj2RecoveryPolicy.ExpectedIdentity,
                _=>throw new Exception()
            };
            if(step.Operation==RamSelfTestOperation.ExecuteFixedTest)Executions++;
            if(step.Operation==RamSelfTestOperation.IdentifyMonitor && BadMonitor)data[7]^=1;
            reply=new(0x21,0x100,data);
        }
        public RecoveryFrame? Read(int timeout){var r=reply;reply=null;return r;}
        public void SendBindingStep(int index)=>throw new Exception("Binding is independently tested");
        public void Open()=>throw new Exception();public void StartEntryPulse()=>throw new Exception();
        public void StopEntryPulse()=>throw new Exception();public void ClearReceive()=>throw new Exception();
        public void RequestIdentity()=>throw new Exception();public void Dispose(){}public string? CleanupWarning=>null;
    }
}
