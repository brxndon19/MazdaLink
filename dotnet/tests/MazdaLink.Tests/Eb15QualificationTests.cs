using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MazdaLink.Core;
using MazdaLink.J2534;

namespace MazdaLink.Tests;

public sealed class Eb15QualificationTests
{
    internal static T Make<T>(params object[] a)=>(T)Activator.CreateInstance(typeof(T),BindingFlags.Instance|BindingFlags.NonPublic,null,a,null)!;
    internal static RestoreQualificationPlan Plan(bool recovery=false)
    {
        byte[] bytes=RestoreQualificationTests.Image();
        return Make<RestoreQualificationPlan>(bytes,Make<RecoveryBindingPlan>(bytes[..0x7100]),Make<OriginalBlockRestorePlan>(bytes,15),recovery,0x12345678u);
    }
    [Fact] public void Eb15ImageAndPlanAreFixedAndRestoreThePointerUnitLast()
    {
        var p=Plan();Assert.Equal(15,p.SelectedBlock);Assert.Equal(0xE0000,p.Original.SelectedStart);
        Assert.Equal(Eb15OriginalRestoreKernel.ImageSha256,p.KernelSha256);Assert.Equal(3012,p.KernelBytes);
        Assert.NotEqual(OriginalRestoreKernel.ImageSha256,p.KernelSha256);Assert.Equal(10243,p.Commands.Count);
        Assert.Equal(15,Assert.Single(p.Commands,e=>e.Phase==RestorePhase.Erase).Command.Index);
        byte[] original=RestoreQualificationTests.Image();var programs=p.Commands.Where(e=>e.Phase==RestorePhase.Program).ToArray();Assert.Equal(1024,programs.Length);
        for(int i=0;i<1024;i++)
        {
            Assert.Equal(i,programs[i].Command.Index);Assert.Equal(0xE0000+i*128,p.Original.Address(i));
            Assert.Equal(original.AsSpan(0xE0000+i*128,128).ToArray(),programs[i].Command.Staging);
        }
        Assert.Equal(0xFFF80,p.Original.Address(programs[^1].Command.Index));
        byte[] copy=p.KernelImage;copy[0]^=1;Assert.Equal(Eb15OriginalRestoreKernel.ImageSha256,RomStorage.Hash(p.KernelImage));
        Assert.Throws<ArgumentOutOfRangeException>(()=>p.Original.Data(1024));
    }
    [Fact] public void Eb15FullWireRunRequiresOneEraseAllUnitsAndTwoExactOriginalReads()
    {
        var p=Plan();using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j);
        var result=new RestoreQualificationRunner().Run(peer,p,_=>{});
        Assert.Equal(1,peer.Erases);Assert.Equal(1024,peer.Programs);Assert.Equal(RestoreQualificationTests.Image(),peer.Bytes);
        Assert.Equal(result.OriginalSha256,result.FinalRead1Sha256);Assert.Equal(result.OriginalSha256,result.FinalRead2Sha256);
        Assert.Equal(("complete",-1),j.Entries[^1]);Assert.Equal(2054,j.Entries.Count);Assert.Throws<TransportException>(()=>peer.Gate.Begin(0));
    }
    [Theory][InlineData(0xC0100)][InlineData(0xDFFFF)][InlineData(0xE0000)][InlineData(0xFF6DC)][InlineData(0xFFFFC)]
    public void FirstEb15TestRejectsEveryChangedCurrentRegionBeforeErase(int address)
    {
        var p=Plan();using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j);peer.Bytes[address]^=1;
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(0,peer.Erases);
    }
    [Theory][InlineData(-1)][InlineData(0)][InlineData(1005)][InlineData(1023)]
    public void UnknownEb15CompletionCannotRetryAndFreshRecoveryReerasesFromUnitZero(int cut)
    {
        var plan=Plan();using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,j){Cut=cut};
        Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));
        int attempts=peer.Programs;Assert.Throws<TransportException>(()=>peer.SendRestoreStep(peer.Next));Assert.Equal(attempts,peer.Programs);
        var recovery=Plan(true);using var fresh=new RestoreQualificationTests.MemoryJournal();using var again=new RestoreQualificationTests.Peer(recovery,fresh,peer.Bytes);
        new RestoreQualificationRunner().Run(again,recovery,_=>{});Assert.Equal(1,again.Erases);Assert.Equal(1024,again.Programs);
        Assert.Equal(("unit-intent",0),fresh.Entries[4]);Assert.Equal(RestoreQualificationTests.Image(),again.Bytes);
    }
    [Theory][InlineData(0xC0091)][InlineData(0xC0100)][InlineData(0xDFFFF)]
    public void Eb15RecoveryNeverIgnoresPreservedEb14Differences(int address)
    {
        var p=Plan(true);using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j);peer.Bytes[address]^=1;
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(0,peer.Erases);
    }
    [Theory][InlineData(0xE0000)][InlineData(0xFF6DC)][InlineData(0xFFFFC)]
    public void BlankVerificationIncludesChecksumAndPointerBeforeAnyProgramming(int address)
    {
        var p=Plan();using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j){CorruptBlankAddress=address};
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(1,peer.Erases);Assert.Equal(0,peer.Programs);
    }
    [Fact] public void Eb15UnitMismatchAndFullFpfrFailureBlockLaterProgramming()
    {
        var p=Plan();using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j){CorruptUnit=true};
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(1,peer.Programs);
        using var j2=new RestoreQualificationTests.MemoryJournal();using var bad=new RestoreQualificationTests.Peer(p,j2){FailEraseFpfr=true};
        Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(bad,p,_=>{}));Assert.Equal(0,bad.Programs);Assert.Equal(0x100u,bad.Gate.LastResult!.FlashResult);
    }
    [Theory][InlineData("erase-intent")][InlineData("unit-intent")][InlineData("unit-verified")]
    public void Eb15DurableJournalFailurePreventsLaterDestructiveRequests(string stage)
    {
        var p=Plan();using var j=new RestoreQualificationTests.MemoryJournal{FailStage=stage};using var peer=new RestoreQualificationTests.Peer(p,j);
        Assert.Throws<IOException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(stage=="erase-intent"?0:1,peer.Erases);
        Assert.Equal(stage=="unit-verified"?1:0,peer.Programs);Assert.Throws<TransportException>(()=>peer.SendRestoreStep(peer.Next));
    }
    [Fact] public void Eb15JournalBindsEveryRecordToBlock15AndRetainsLegacyFormat()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"journal.jsonl");var p=Plan();
            using(var real=new RestoreJournal(path,p.Original))using(var j=new RestoreQualificationTests.MemoryJournal{Mirror=real})using(var peer=new RestoreQualificationTests.Peer(p,j))
                new RestoreQualificationRunner().Run(peer,p,_=>{});
            var lines=File.ReadAllLines(path);string previous=new('0',64);Assert.Equal(2054,lines.Length);
            for(int i=0;i<lines.Length;i++)
            {
                var e=JsonSerializer.Deserialize<RestoreJournalEntry>(lines[i])!;Assert.Equal(2,e.Schema);Assert.Equal(15,e.Block);Assert.Equal(i+1,e.Sequence);
                Assert.Equal(previous,e.PreviousHash);previous=RomStorage.Hash(Encoding.UTF8.GetBytes(lines[i]));
            }
            Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireJournal(path,p.Original.OriginalSha256,false));
            string old=Path.Combine(root,"old.jsonl");using(var j=new RestoreJournal(old,RestoreQualificationTests.Plan().Original))j.Commit("started");
            var first=JsonSerializer.Deserialize<RestoreJournalEntry>(File.ReadAllText(old))!;Assert.Equal(1,first.Schema);Assert.Null(first.Block);Assert.DoesNotContain("Block",File.ReadAllText(old));
        }finally{Directory.Delete(root,true);}
    }
    [Fact] public void NativeChannelRejectsAJournalForTheOtherBlockBeforeDriverAccess()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var p=Plan();using var journal=new RestoreJournal(Path.Combine(root,"wrong.jsonl"),RestoreQualificationTests.Plan().Original);
            Assert.Equal(p.Original.OriginalSha256,journal.OriginalSha256);Assert.Equal(14,journal.SelectedBlock);
            Assert.Throws<InvalidDataException>(()=>new NativeOriginalRestoreChannel(null!,p,journal));
        }finally{Directory.Delete(root,true);}
    }
    [Fact] public void Eb15CannotAcquireTheEb14ControlledStopOrOfflineDeviceContract()
    {
        var p=Plan();using var journal=new RestoreQualificationTests.MemoryJournal();
        Assert.Throws<TargetInvocationException>(()=>Make<TransactionCommand>(TransactionOpcode.Erase,(ushort)1,1u,(ushort)15,null!));
        Assert.Throws<InvalidOperationException>(()=>new OriginalRestoreTransaction(p.Original,new BlockedDevice(),journal).Run());
        Assert.Throws<TargetInvocationException>(()=>Make<RestoreQualificationPlan>(RestoreQualificationTests.Image(),p.Binding,p.Original,false,1u,ControlledRecoveryStage.Prepare));
        Assert.Throws<InvalidDataException>(()=>RestoreQualificationPlan.CreateEb15(new(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId),"missing",false));
    }
    private sealed class BlockedDevice:IOriginalRestoreDevice
    {
        public void ColdBind(string hash)=>throw new Exception("Wrong-block device must never run");
        public byte[] ReadImage()=>throw new Exception();public void EraseEb14()=>throw new Exception();
        public void ProgramNextOriginalUnit(int i,byte[] b)=>throw new Exception();public byte[] ReadUnit(int i)=>throw new Exception();
    }
    [Fact] public void MissingEb15PrerequisitesAndExternalOverridesNeverOpenHardware()
    {
        int opens=0;var t=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),originalRestore:(_,_)=>{opens++;throw new Exception();});
        foreach(string c in new[]{"eb15-original-restore-check","eb15-original-restore-recover"})
        {Assert.Equal(1,t.Execute([c,"missing/original.rom","missing/run"]));Assert.Equal(1,t.Execute([c,"original.rom","run","modified.rom"]));}
        Assert.Equal(0,opens);
    }
}
