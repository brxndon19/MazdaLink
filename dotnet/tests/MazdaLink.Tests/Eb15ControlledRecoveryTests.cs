using System.Text.Json;
using System.Text.Json.Nodes;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class Eb15ControlledRecoveryTests
{
    internal static RestoreQualificationPlan Plan(ControlledRecoveryStage stage)
    {
        byte[] bytes=RestoreQualificationTests.Image();var full=Eb15QualificationTests.Plan();
        return Eb15QualificationTests.Make<RestoreQualificationPlan>(bytes,full.Binding,full.Original,stage==ControlledRecoveryStage.Recover,0x12345678u,stage,true);
    }
    [Fact] public void FixedEb15BoundaryPreservesEb14AndErasesChecksumTableAndBootPointer()
    {
        var p=Plan(ControlledRecoveryStage.Prepare);Assert.Equal(15,p.SelectedBlock);Assert.Equal(6149,p.Commands.Count);Assert.Equal(1,p.ExpectedUnits);
        Assert.Equal(15,Assert.Single(p.Commands,e=>e.Phase==RestorePhase.Erase).Command.Index);Assert.Equal(0,Assert.Single(p.Commands,e=>e.Phase==RestorePhase.Program).Command.Index);
        Assert.Equal(Eb15OriginalRestoreKernel.ImageSha256,p.KernelSha256);Assert.Equal(RestorePhase.FinishPartial,p.Commands[^1].Phase);
        byte[] expected=p.ExpectedPartialImage(),original=RestoreQualificationTests.Image();
        Assert.Equal(original[..0xE0080],expected[..0xE0080]);Assert.All(expected[0xE0080..],b=>Assert.Equal(255,b));
        Assert.Equal(Eb15ControlledRecoveryEvidence.ExpectedPartial(original),expected);Assert.Equal(original.AsSpan(0xC0091,16).ToArray(),expected.AsSpan(0xC0091,16).ToArray());
        Assert.DoesNotContain(p.Commands,e=>e.Phase==RestorePhase.FinalReadback1);
    }
    [Fact] public void Eb15PartialStopHasItsOwnResultAndCannotClaimCompleteRestorationOrErasedCalibrationId()
    {
        var p=Plan(ControlledRecoveryStage.Prepare);using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j);
        Assert.Throws<InvalidOperationException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));
        Assert.Throws<InvalidOperationException>(()=>new RestoreQualificationRunner().RunToControlledStop(peer,p,_=>{}));Assert.Equal(0,peer.Erases);
        var result=new RestoreQualificationRunner().RunEb15ToControlledStop(peer,p,_=>{});
        Assert.True(result.BootValidityPointerErased);Assert.True(result.FinalMonitorPassed);Assert.Equal(1,result.OriginalUnits);
        Assert.Equal(p.ExpectedPartialImage(),peer.Bytes);Assert.Equal(("controlled-stop",-1),j.Entries[^1]);Assert.Equal(8,j.Entries.Count);
        Assert.DoesNotContain(j.Entries,e=>e.Stage=="complete");Assert.Equal(1u,peer.Gate.LastResult!.NextUnit);
    }
    [Fact] public void Eb15ColdRecoveryChecksExactPartialImageThenReerasesAndRestoresEveryOriginalUnit()
    {
        var p=Plan(ControlledRecoveryStage.Recover);using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j,p.ExpectedPartialImage());
        var result=new RestoreQualificationRunner().Run(peer,p,_=>{});Assert.Equal(1,peer.Erases);Assert.Equal(1024,peer.Programs);
        Assert.Equal(("unit-intent",0),j.Entries[4]);Assert.Equal(RestoreQualificationTests.Image(),peer.Bytes);Assert.Equal(result.OriginalSha256,result.FinalRead2Sha256);
    }
    [Theory][InlineData(0x7100)][InlineData(0xC0091)][InlineData(0xDFFFF)][InlineData(0xE0000)][InlineData(0xE0080)][InlineData(0xFF650)][InlineData(0xFF6DC)][InlineData(0xFFFFC)]
    public void AnyUnexpectedPartialOrPreservedByteStopsBeforeRecoveryErase(int address)
    {
        var p=Plan(ControlledRecoveryStage.Recover);byte[] bytes=p.ExpectedPartialImage();bytes[address]^=1;
        using var j=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(p,j,bytes);
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,p,_=>{}));Assert.Equal(0,peer.Erases);Assert.Equal(0,peer.Programs);
    }
    [Fact] public void Alpha18PrerequisiteRequiresBothOriginalCapturesAndTheBlock15Journal()
    {
        using var f=new Fixture();Eb15ControlledRecoveryEvidence.RequireOriginalRestoration(f.Root,f.Backup);
        foreach(string n in new[]{"final-pass-1.read","final-pass-2.read","transaction.jsonl"})
        {
            string p=Path.Combine(f.Root,n);byte[] original=File.ReadAllBytes(p);File.WriteAllBytes(p,[]);
            Assert.ThrowsAny<Exception>(()=>Eb15ControlledRecoveryEvidence.RequireOriginalRestoration(f.Root,f.Backup));File.WriteAllBytes(p,original);
        }
        string events=Path.Combine(f.Root,"events.jsonl");var lines=File.ReadAllLines(events);var bad=JsonNode.Parse(lines[^1])!;bad["recovery"]=true;lines[^1]=bad.ToJsonString();File.WriteAllLines(events,lines);
        Assert.Throws<InvalidDataException>(()=>Eb15ControlledRecoveryEvidence.RequireOriginalRestoration(f.Root,f.Backup));
    }
    [Fact] public void ReceiptRequiresExactEb15CaptureAndEightBlock15RecordsBoundToTheCompletedRestoration()
    {
        using var f=new Fixture();string boundary=f.WriteBoundary();Eb15ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup,f.Root);
        string source=Path.Combine(f.Root,"events.jsonl");var lines=File.ReadAllLines(source);var x=JsonNode.Parse(lines[0])!;x["extra"]=true;lines[0]=x.ToJsonString();File.WriteAllLines(source,lines);
        Eb15ControlledRecoveryEvidence.RequireOriginalRestoration(f.Root,f.Backup);
        Assert.Throws<InvalidDataException>(()=>Eb15ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup,f.Root));
    }
    [Fact] public void WrongBlockPointerOrCaptureCannotAuthorizeEb15BoundaryResume()
    {
        using var f=new Fixture();string boundary=f.WriteBoundary();string path=Path.Combine(boundary,"boundary.json");string original=File.ReadAllText(path);
        Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup));
        foreach(var (key,value) in new(string,JsonNode?)[]{("EraseBlock",JsonValue.Create(14)),("BootValidityPointerErased",JsonValue.Create(false)),("ActiveOperationInterrupted",JsonValue.Create(true))})
        {
            var r=JsonNode.Parse(original)!;r[key]=value;File.WriteAllText(path,r.ToJsonString());
            Assert.Throws<InvalidDataException>(()=>Eb15ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup,f.Root));
        }
        File.WriteAllText(path,original);byte[] data=File.ReadAllBytes(Path.Combine(boundary,"partial.read"));data[0xFFFFC]=0;
        File.WriteAllBytes(Path.Combine(boundary,"partial.read"),data);Assert.Throws<InvalidDataException>(()=>Eb15ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup,f.Root));
        File.WriteAllBytes(Path.Combine(boundary,"partial.read"),Eb15ControlledRecoveryEvidence.ExpectedPartial(f.Backup.Data));
        string journal=Path.Combine(boundary,"prepare","transaction.jsonl");var records=File.ReadAllLines(journal);File.WriteAllLines(journal,records[..^1]);
        Assert.Throws<InvalidDataException>(()=>Eb15ControlledRecoveryEvidence.RequireBoundary(boundary,f.Backup,f.Root));
    }
    [Fact] public void MissingEvidenceAndCodeBlockOrUnitOverridesNeverOpenEitherBackend()
    {
        Assert.Throws<ArgumentException>(()=>RestoreQualificationPlan.CreateEb15Controlled(new(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId),"missing",ControlledRecoveryStage.None));
        Assert.Throws<ArgumentException>(()=>RestoreQualificationPlan.CreateEb15Controlled(new(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId),"missing",(ControlledRecoveryStage)99));
        int opens=0;var t=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),originalRestore:(_,_)=>{opens++;throw new Exception();});
        Assert.Equal(1,t.Execute(["eb15-controlled-recovery-check","missing/original.rom","missing/run"]));
        Assert.Equal(1,t.Execute(["eb15-controlled-recovery-resume","missing/original.rom","missing/run","missing/boundary"]));
        Assert.Equal(1,t.Execute(["eb15-controlled-recovery-check","original.rom","run","modified.rom"]));
        Assert.Equal(1,t.Execute(["eb15-controlled-recovery-resume","original.rom","run","boundary","--unit=1"]));Assert.Equal(0,opens);
    }
    private sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        public VerifiedRawBackup Backup {get;}=new(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId);
        public Fixture()
        {
            Directory.CreateDirectory(Root);var p=Eb15QualificationTests.Plan();string hash=p.Original.OriginalSha256;
            foreach(string n in new[]{"final-pass-1.read","final-pass-2.read"})File.WriteAllBytes(Path.Combine(Root,n),Backup.Data);
            Journal(Path.Combine(Root,"transaction.jsonl"),false);
            var stages=new Dictionary<int,string>{{2,"intact-normal-preflight-passed-before-first-erase-test"},{24,"BeforeErase-blocks-verified: 2048"},{25,"erase-result-and-busy-callback-observed-before-full-blank-preserved-readback"},{41,"EraseReadback-blocks-verified: 2048"},{73,"original-units-verified: 1024"},{114,"normal-restart-confirmed-after-original-restoration"},{115,"eb15-original-restoration-complete-cold-and-active-recovery-not-qualified"}};
            var rows=Enumerable.Range(1,115).Select(i=>new {schema=14,sequence=i,version="0.9.0-alpha.18+c238847d4b0adab62a0143c4146ba84c8f75037e",targetProfile=Zyj2RecoveryPolicy.SoftwareId,
                backupSha256=hash,eraseBlock=15,blockStart=0xE0000,blockSha256=p.Original.BlockSha256,expectedBootSha256=p.Original.PreservedBootSha256,kernelSha256=Eb15OriginalRestoreKernel.ImageSha256,kernelBytes=3012,
                recovery=false,normalDiagnosticsRequiredBeforeEntry=true,originalOnlyEraseProgrammingImplemented=true,modifiedFlashingEnabled=false,interruptedWriteRecoveryQualified=false,
                stage=stages.GetValueOrDefault(i,"synthetic-progress"),result=new{commands=10243,originalUnits=1024,finalMonitorPassed=true,originalSha256=hash,finalRead1Sha256=hash,finalRead2Sha256=hash},
                transport=new{sentSteps=739420,commandsCompleted=10243,originalUnitsVerified=1024,bootBytes=28928,kernelBytes=3012,stateBytes=48,failed=false,awaitingReply=false},
                lastResult=new{opcode=i==25?33:127,sequence=i==25?2050:10243,index=i==25?15:0,status=0,flashResult=0,flags=99,nextUnit=i==25?0:1024,fccs=0}});
            File.WriteAllLines(Path.Combine(Root,"events.jsonl"),rows.Select(x=>JsonSerializer.Serialize(x)));
        }
        private void Journal(string path,bool partial)
        {
            using var j=new RestoreJournal(path,Eb15QualificationTests.Plan().Original);j.Commit("started");j.Commit("bound");j.Commit("erase-intent");j.Commit("erase-verified");
            for(int i=0;i<(partial?1:1024);i++){j.Commit("unit-intent",i);j.Commit("unit-verified",i);}
            j.Commit(partial?"partial-readback":"full-readback-1");j.Commit(partial?"controlled-stop":"complete");
        }
        public string WriteBoundary()
        {
            string b=Path.Combine(Root,"boundary");Directory.CreateDirectory(Path.Combine(b,"prepare"));byte[] partial=Eb15ControlledRecoveryEvidence.ExpectedPartial(Backup.Data);
            File.WriteAllBytes(Path.Combine(b,"partial.read"),partial);Journal(Path.Combine(b,"prepare","transaction.jsonl"),true);
            File.WriteAllText(Path.Combine(b,"boundary.json"),JsonSerializer.Serialize(new Eb15ControlledBoundaryReceipt(1,15,RomStorage.Hash(Backup.Data),RomStorage.Hash(partial),Eb15OriginalRestoreKernel.ImageSha256,1,true,true,false,
                RomStorage.Hash(File.ReadAllBytes(Path.Combine(Root,"events.jsonl"))),RomStorage.Hash(File.ReadAllBytes(Path.Combine(Root,"transaction.jsonl"))))));return b;
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
}
