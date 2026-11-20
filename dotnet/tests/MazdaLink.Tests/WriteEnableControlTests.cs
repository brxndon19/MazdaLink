using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class WriteEnableControlTests
{
    [Fact]
    public void ControlPlanBindsPreservedBootBeforeOnlyItsImmutableFixedImage()
    {
        var binding=RecoveryBindingTests.SyntheticPlan();var plan=new WriteEnableControlPlan(binding);
        Assert.Equal(752,Zyj2WriteEnableControlPolicy.Image.Length);
        Assert.Equal(Zyj2WriteEnableControlPolicy.ImageSha256,RomStorage.Hash(Zyj2WriteEnableControlPolicy.Image));
        Assert.Equal(59491,plan.Count);
        Assert.Equal(RamSelfTestOperation.IdentifyMonitor,plan[binding.Count-1].Operation);
        Assert.Equal(RamSelfTestOperation.Address,plan[binding.Count].Operation);
        Assert.Equal(1,plan.Count(s=>s.Operation==RamSelfTestOperation.ExecuteFixedTest));
        var image=Zyj2WriteEnableControlPolicy.Image;image[0]^=1;
        Assert.NotEqual(image,Zyj2WriteEnableControlPolicy.Image);
        var first=Zyj2WriteEnableControlPolicy.Steps[0].Payload;first[2]^=1;
        Assert.NotEqual(first,Zyj2WriteEnableControlPolicy.Steps[0].Payload);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan[plan.Count]);
    }
    [Fact]
    public void ControlRunnerVerifiesEveryByteAndRecordsResultBeforeMonitorReturn()
    {
        using var peer=new Peer();var stages=new List<string>();
        var result=new Zyj2WriteEnableControl().Run(peer,stages.Add);
        Assert.True(result.Passed);Assert.True(result.RestorationConfirmed);
        Assert.Equal(752,result.UploadedBytes);Assert.Equal(752,result.VerifiedBytes);
        Assert.Equal(1,peer.Executions);Assert.Equal(1634,peer.Sent);
        Assert.Equal("monitor-return-confirmed-after-write-enable-control",stages.Last());
    }
    [Theory]
    [InlineData(0)] [InlineData(375)] [InlineData(751)]
    public void AnyControlByteMismatchPreventsExecution(int bad)
    {
        using var peer=new Peer{BadByte=bad};
        Assert.Throws<TransportException>(()=>new Zyj2WriteEnableControl().Run(peer,_=>{}));
        Assert.Equal(0,peer.Executions);
    }
    [Fact]
    public void CancelledControlCannotAdvanceToExecution()
    {
        using var cancel=new CancellationTokenSource();using var peer=new Peer{OnSend=()=>cancel.Cancel()};
        Assert.Throws<OperationCanceledException>(()=>new Zyj2WriteEnableControl().Run(peer,_=>{},cancel.Token));
        Assert.Equal(0,peer.Executions);Assert.Equal(1,peer.Sent);
    }
    [Fact]
    public void ControlResultRemainsObservableIfFinalMonitorFails()
    {
        using var peer=new Peer{BadMonitor=true};WriteEnableControlResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2WriteEnableControl().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.NotNull(result);Assert.True(result.Passed);
    }
    [Fact]
    public void UncertainRestorationRetainsResultAndStopsBeforeAnyMonitorQuery()
    {
        using var peer=new Peer{UnsafeRestoration=true};WriteEnableControlResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2WriteEnableControl().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.NotNull(result);Assert.Equal(8,result.Status);Assert.False(result.RestorationConfirmed);
        Assert.Equal(Zyj2WriteEnableControlPolicy.Steps.Count-1,peer.Sent);
    }
    [Fact]
    public void WrongControlResultMarkerCannotBecomeEvidence()
    {
        using var peer=new Peer{BadMarker=true};WriteEnableControlResult? result=null;
        Assert.Throws<TransportException>(()=>new Zyj2WriteEnableControl().Run(peer,_=>{},observeResult:r=>result=r));
        Assert.Null(result);
    }
    [Theory]
    [InlineData(1,128,0,31)] [InlineData(2,128,0,31)] [InlineData(3,128,0,31)]
    [InlineData(4,128,0,31)] [InlineData(5,128,0,31)] [InlineData(6,128,0,31)]
    [InlineData(7,128,0,31)] [InlineData(8,128,0,31)] [InlineData(255,128,0,31)]
    [InlineData(0,0,0,31)] [InlineData(0,144,0,31)] [InlineData(0,128,128,31)]
    [InlineData(0,128,0,29)] [InlineData(0,128,0,255)]
    public void StatusOrIncompleteObservationsCannotQualifyControl(byte status,byte during,byte after,byte flags)
    {Assert.False(new WriteEnableControlResult(752,752,status,during,after,flags).Passed);}
    [Fact]
    public void MissingBackupsAndExternalOverridesCannotOpenControlHardware()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            writeEnableControl:_=>{opens++;throw new Exception();});
        string missing=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"original.rom");
        Assert.Equal(1,app.Execute(["write-enable-check",missing]));
        Assert.Equal(1,app.Execute(["write-enable-check",missing,"external.bin"]));
        Assert.Equal(1,app.Execute(["write-enable-check",missing,"0xfffff740"]));
        Assert.Equal(0,opens);
    }
    private sealed class Peer:IWriteEnableControlChannel
    {
        public int Sent,Executions; public int BadByte{get;init;}=-1;
        public bool UnsafeRestoration{get;init;}public bool BadMonitor{get;init;}public bool BadMarker{get;init;}public Action? OnSend{get;init;}
        private RecoveryFrame? reply;
        public void SendControlStep(int index)
        {
            Assert.Equal(Sent++,index);Assert.Null(reply);OnSend?.Invoke();
            var step=Zyj2WriteEnableControlPolicy.Steps[index];
            if(!step.ExpectsReply)return;
            byte[] data=step.Operation switch
            {
                RamSelfTestOperation.Address=>step.Payload,
                RamSelfTestOperation.VerifyByte=>[0x7A,0xB1,(byte)(Zyj2WriteEnableControlPolicy.Image[step.ImageOffset]^(step.ImageOffset==BadByte?1:0)),0,0,0,0,0],
                RamSelfTestOperation.ExecuteFixedTest=>[0x7A,0xC6,0x4D,BadMarker?(byte)0x49:(byte)0x57,UnsafeRestoration?(byte)8:(byte)0,128,UnsafeRestoration?(byte)128:(byte)0,31],
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
