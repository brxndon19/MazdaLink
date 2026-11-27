using System.Reflection;
using System.Text;
using System.Text.Json;
using MazdaLink.Core;
using MazdaLink.J2534;
namespace MazdaLink.Tests;

public sealed class ModifiedFlashTests
{
    private static T Make<T>(params object?[] args)=>(T)Activator.CreateInstance(typeof(T),BindingFlags.Instance|BindingFlags.NonPublic,null,args,null)!;
    internal static byte[] Original()=>(byte[])typeof(Zyj2IntegrityTests).GetMethod("Image",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
    internal static byte[] Intended(byte[] original)
    {var edit=original.ToArray();edit[0xD54F0]^=1;edit[0xDFFFF]^=1;return Zyj2Integrity.Correct(edit);}
    internal static RestoreQualificationPlan Plan(byte[] original,byte[] desired,int block,bool recovery=false,byte[]? preceding=null,string id="01234567890123456789012345678901")
    {
        var step=Make<ModifiedFlashStep>(id,original,desired,block,recovery,preceding);
        return Make<RestoreQualificationPlan>(original,Make<RecoveryBindingPlan>(original.AsSpan(0,0x7100).ToArray()),
            Make<OriginalBlockRestorePlan>(original,block),recovery,0x98765432u,ControlledRecoveryStage.None,false,step);
    }
    private static byte[] Run(RestoreQualificationPlan plan,byte[] current)
    {
        using var journal=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,journal,current);
        var result=new RestoreQualificationRunner().Run(peer,plan,_=>{});
        Assert.Equal(1,peer.Erases);Assert.Equal(1024,peer.Programs);Assert.True(peer.Gate.Complete);
        Assert.Equal(2054,journal.Entries.Count);Assert.True(plan.Modified!.FinalReadsComplete);
        Assert.Equal(RomStorage.Hash(peer.Bytes),result.FinalRead1Sha256);Assert.Equal(result.FinalRead1Sha256,result.FinalRead2Sha256);
        return peer.Bytes.ToArray();
    }
    [Fact]public void BothFixedBlocksProduceExactModifiedImageAndPreserveThePointerUntilItsLastUnit()
    {
        byte[] original=Original(),desired=Intended(original);var first=Plan(original,desired,14);
        byte[] intermediate=Run(first,original);Assert.Equal(original.AsSpan(0xE0000).ToArray(),intermediate.AsSpan(0xE0000).ToArray());
        Assert.Equal(desired.AsSpan(0xC0000,0x20000).ToArray(),intermediate.AsSpan(0xC0000,0x20000).ToArray());
        var second=Plan(original,desired,15,preceding:intermediate);
        Assert.Equal(1023,second.Commands.Last(e=>e.Phase==RestorePhase.Program).Command.Index);
        byte[] actual=Run(second,intermediate);Assert.Equal(desired,actual);Assert.All(Zyj2Integrity.Check(actual),r=>Assert.True(r.Valid));
        Assert.Equal(original.AsSpan(0,0xC0000).ToArray(),actual.AsSpan(0,0xC0000).ToArray());
        Assert.Equal(OriginalRestoreKernel.ImageSha256,first.KernelSha256);Assert.Equal(Eb15OriginalRestoreKernel.ImageSha256,second.KernelSha256);
    }
    [Fact]public void SubsequentValidTuneUsesAFrozenCurrentSnapshotAndRejectsAnInvalidCurrentChecksum()
    {
        byte[] original=Original(),current=Intended(original),desired=current.ToArray();desired[0xC0123]^=2;desired=Zyj2Integrity.Correct(desired);
        var first=Plan(original,desired,14);byte[] intermediate=Run(first,current);
        Assert.Equal(RomStorage.Hash(current),first.Modified!.BeforeSha256);
        Assert.Equal(current.AsSpan(0xE0000).ToArray(),intermediate.AsSpan(0xE0000).ToArray());
        Assert.Equal(desired,Run(Plan(original,desired,15,preceding:intermediate),intermediate));
        current[0xC0124]^=1;using var j=new RestoreQualificationTests.MemoryJournal();var invalid=Plan(original,desired,14);using var peer=new RestoreQualificationTests.Peer(invalid,j,current);
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,invalid,_=>{}));
        Assert.Equal(0,peer.Erases);
    }
    [Theory][InlineData(14,-1)][InlineData(14,0)][InlineData(14,1023)][InlineData(15,-1)][InlineData(15,0)][InlineData(15,1023)]
    public void UnknownCompletionNeverRetriesAndColdRecoveryReerasesBothWholeBlocks(int block,int cut)
    {
        byte[] original=Original(),desired=Intended(original),current=original;
        if(block==15)current=Run(Plan(original,desired,14),current);
        var stopped=Plan(original,desired,block,preceding:block==15?current:null);
        using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(stopped,j,current){Cut=cut};
        Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(peer,stopped,_=>{}));int units=peer.Programs;
        Assert.Throws<TransportException>(()=>peer.Gate.Begin(peer.Next));Assert.Equal(units,peer.Programs);
        byte[] intermediate=Run(Plan(original,original,14,true),peer.Bytes);
        byte[] restored=Run(Plan(original,original,15,true,intermediate),intermediate);Assert.Equal(original,restored);
    }
    [Theory][InlineData(0x7200)][InlineData(0xBFFFF)]
    public void RecoveryCannotIgnoreAnyPreservedProgramDifference(int address)
    {
        byte[] original=Original(),current=original.ToArray();current[address]^=1;
        var plan=Plan(original,original,14,true);using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,j,current);
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));Assert.Equal(0,peer.Erases);
    }
    [Fact]public void FrozenOtherBlockCannotChangeAfterBindingOrBetweenBlockWrites()
    {
        byte[] original=Original(),desired=Intended(original);var first=Plan(original,desired,14);
        using(var j=new RestoreQualificationTests.MemoryJournal())using(var peer=new RestoreQualificationTests.Peer(first,j,original){CorruptBlankAddress=0xE0100})
        {Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,first,_=>{}));Assert.Equal(0,peer.Programs);}
        byte[] intermediate=Run(Plan(original,desired,14),original),changed=intermediate.ToArray();changed[0xD54F0]^=1;
        var second=Plan(original,desired,15,preceding:intermediate);using var j2=new RestoreQualificationTests.MemoryJournal();using var peer2=new RestoreQualificationTests.Peer(second,j2,changed);
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer2,second,_=>{}));Assert.Equal(0,peer2.Erases);
    }
    [Theory][InlineData("erase-intent")][InlineData("unit-intent")][InlineData("unit-verified")]
    public void ModifiedJournalFailureStopsAllLaterDestructiveRequests(string stage)
    {
        byte[] original=Original();var plan=Plan(original,Intended(original),14);using var j=new RestoreQualificationTests.MemoryJournal{FailStage=stage};using var peer=new RestoreQualificationTests.Peer(plan,j,original);
        Assert.Throws<IOException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));
        Assert.Equal(stage=="erase-intent"?0:1,peer.Erases);Assert.Equal(stage=="unit-verified"?1:0,peer.Programs);
        Assert.Throws<TransportException>(()=>peer.Gate.Begin(peer.Next));
    }
    [Fact]public void OriginalOnlyNativeCapabilityRejectsModifiedPlansBeforeDriverAccess()
    {
        byte[] original=Original();var plan=Plan(original,Intended(original),14);string path=Path.GetTempFileName();File.Delete(path);
        try{using var journal=new RestoreJournal(path,plan.Original);Assert.Throws<InvalidDataException>(()=>new NativeOriginalRestoreChannel(null!,plan,journal));}
        finally{File.Delete(path);}
    }
    [Fact]public void PublicFactoryRequiresExaminedBackupAndNeverAcceptsAnotherCalibration()
    {
        byte[] original=Original();Assert.Throws<InvalidDataException>(()=>ModifiedFlashSequence.Create(new(original,Zyj2RecoveryPolicy.SoftwareId),Intended(original),false));
        Assert.Throws<InvalidDataException>(()=>ModifiedFlashSequence.Create(new(original,TargetPolicy.SoftwareId),original,false));
    }
    [Fact]public void DedicatedCommandsRejectMissingFilesAndOverridesBeforeAnyHardwareOpen()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            modifiedFlash:(_,_,_)=>{opens++;throw new Exception();});
        Assert.Equal(1,app.Execute(["modified-spare-flash","missing/original.rom","missing/tune.bin"]));
        Assert.Equal(1,app.Execute(["modified-spare-recover","missing/original.rom"]));
        Assert.Equal(1,app.Execute(["modified-spare-flash","a","b","--address","0"]));
        Assert.Equal(1,app.Execute(["modified-spare-recover","a","tune.bin"]));Assert.Equal(0,opens);
    }
    [Fact]public void CombinedJournalBindsBothImagesLiveSnapshotBlockOrderAndHashChain()
    {
        byte[] original=Original(),desired=Intended(original);var tx=Make<ModifiedFlashSequence>(new VerifiedRawBackup(original,Zyj2RecoveryPolicy.SoftwareId),desired,false);
        string path=Path.GetTempFileName();File.Delete(path);
        try
        {
            using(var journal=new ModifiedFlashJournal(path,tx))
            {
                byte[] current=original;
                foreach(int block in new[]{14,15})
                {
                    var plan=Plan(original,desired,block,preceding:block==15?current:null,id:tx.Id);journal.BindStep(plan);
                    using var memory=new RestoreQualificationTests.MemoryJournal{Mirror=journal};using var peer=new RestoreQualificationTests.Peer(plan,memory,current);
                    new RestoreQualificationRunner().Run(peer,plan,_=>{});current=peer.Bytes.ToArray();
                }
            }
            var lines=File.ReadAllLines(path);Assert.Equal(4108,lines.Length);string previous=new('0',64);
            for(int i=0;i<lines.Length;i++)
            {
                using var doc=JsonDocument.Parse(lines[i]);var r=doc.RootElement;
                Assert.Equal(i+1,r.GetProperty("Sequence").GetInt32());Assert.Equal(previous,r.GetProperty("PreviousHash").GetString());
                Assert.Equal(RomStorage.Hash(original),r.GetProperty("OriginalSha256").GetString());Assert.Equal(RomStorage.Hash(desired),r.GetProperty("IntendedSha256").GetString());
                Assert.Equal(i<2054?14:15,r.GetProperty("Block").GetInt32());
                if(r.GetProperty("Stage").GetString()=="erase-intent"){Assert.Equal(64,r.GetProperty("BeforeSha256").GetString()!.Length);Assert.Equal(64,r.GetProperty("AfterSha256").GetString()!.Length);}
                previous=RomStorage.Hash(Encoding.UTF8.GetBytes(lines[i]));
            }
        }finally{File.Delete(path);}
    }
}
