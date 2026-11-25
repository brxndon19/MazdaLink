using System.Reflection;
using System.Text.Json;
using MazdaLink.Core;
namespace MazdaLink.Tests;

public sealed class ControlledRecoveryTests
{
    internal static RestoreQualificationPlan Plan(ControlledRecoveryStage stage)
    {
        byte[] data=RestoreQualificationTests.Image();T Make<T>(params object[] a)=>(T)Activator.CreateInstance(typeof(T),BindingFlags.Instance|BindingFlags.NonPublic,null,a,null)!;
        return Make<RestoreQualificationPlan>(data,Make<RecoveryBindingPlan>(data.AsSpan(0,0x7100).ToArray()),Make<OriginalBlockRestorePlan>(data),stage==ControlledRecoveryStage.Recover,0x12345678u,stage);
    }
    [Fact] public void FixedBoundaryContainsExactlyOneOriginalUnitAndAnErasedCalibrationId()
    {
        var plan=Plan(ControlledRecoveryStage.Prepare);Assert.Equal(6149,plan.Commands.Count);Assert.Equal(1,plan.ExpectedUnits);
        var program=Assert.Single(plan.Commands,e=>e.Phase==RestorePhase.Program);Assert.Equal(0,program.Command.Index);
        Assert.Equal(RestorePhase.FinishPartial,plan.Commands[^1].Phase);Assert.DoesNotContain(plan.Commands,e=>e.Phase==RestorePhase.FinalReadback1);
        byte[] partial=plan.ExpectedPartialImage(),original=RestoreQualificationTests.Image();
        Assert.Equal(original[..0xC0080],partial[..0xC0080]);Assert.All(partial.AsSpan(0xC0080,0x1FF80).ToArray(),b=>Assert.Equal(255,b));Assert.Equal(original[0xE0000..],partial[0xE0000..]);
    }
    [Fact] public void PartialStopCannotBeReportedAsACompleteOriginalRestoration()
    {
        var plan=Plan(ControlledRecoveryStage.Prepare);using var journal=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,journal);
        Assert.Throws<InvalidOperationException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));Assert.Equal(0,peer.Erases);
        var result=new RestoreQualificationRunner().RunToControlledStop(peer,plan,_=>{});
        Assert.Equal(1,result.OriginalUnits);Assert.True(result.CalibrationIdErased);Assert.True(result.FinalMonitorPassed);
        Assert.Equal(plan.ExpectedPartialImage(),peer.Bytes);Assert.Equal(8,journal.Entries.Count);Assert.Equal(("controlled-stop",-1),journal.Entries.Last());
        Assert.DoesNotContain(journal.Entries,e=>e.Stage=="complete");Assert.Equal(1u,peer.Gate.LastResult!.NextUnit);
    }
    [Fact] public void ColdRecoveryFromTheExactPartialImageAlwaysErasesAndStartsAtUnitZero()
    {
        var plan=Plan(ControlledRecoveryStage.Recover);using var journal=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,journal,plan.ExpectedPartialImage());
        var result=new RestoreQualificationRunner().Run(peer,plan,_=>{});Assert.Equal(1,peer.Erases);Assert.Equal(1024,peer.Programs);
        Assert.Equal(RestoreQualificationTests.Image(),peer.Bytes);Assert.Equal(result.OriginalSha256,result.FinalRead2Sha256);
        Assert.Equal(("erase-intent",-1),journal.Entries[2]);Assert.Equal(("unit-intent",0),journal.Entries[4]);
    }
    [Theory] [InlineData(0xC0000)] [InlineData(0xC0080)] [InlineData(0xC0091)] [InlineData(0xDFFFF)] [InlineData(0xE0000)]
    public void UnexpectedPartialOrPreservedBytesStopBeforeRecoveryErase(int address)
    {
        var plan=Plan(ControlledRecoveryStage.Recover);byte[] bytes=plan.ExpectedPartialImage();bytes[address]^=1;
        using var journal=new RestoreQualificationTests.MemoryJournal();using var peer=new RestoreQualificationTests.Peer(plan,journal,bytes);
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));Assert.Equal(0,peer.Erases);Assert.Equal(0,peer.Programs);
    }
    [Fact] public void AFailedSessionCannotAdvanceItsJournalThroughLateReplies()
    {
        var plan=Plan(ControlledRecoveryStage.Prepare);using var journal=new RestoreQualificationTests.MemoryJournal();var gate=new RestoreQualificationGate(plan,journal);
        var step=gate.Begin(0);gate.Fail();Assert.Throws<TransportException>(()=>gate.Observe(new(0x21,0x100,step.Payload)));Assert.Single(journal.Entries);
    }
    [Fact] public void ValidBoundaryReceiptRequiresExactPartialCaptureAndAnEightRecordJournal()
    {
        using var fixture=new EvidenceFolder();var plan=Plan(ControlledRecoveryStage.Prepare);
        using(var real=new RestoreJournal(Path.Combine(fixture.Path,"prepare","transaction.jsonl"),plan.Original))
        using(var memory=new RestoreQualificationTests.MemoryJournal{Mirror=real})using(var peer=new RestoreQualificationTests.Peer(plan,memory))
            new RestoreQualificationRunner().RunToControlledStop(peer,plan,_=>{},observePartial:b=>File.WriteAllBytes(Path.Combine(fixture.Path,"partial.read"),b));
        var receipt=new ControlledBoundaryReceipt(1,plan.Original.OriginalSha256,RomStorage.Hash(plan.ExpectedPartialImage()),OriginalRestoreKernel.ImageSha256,1,true,false);
        File.WriteAllText(Path.Combine(fixture.Path,"boundary.json"),JsonSerializer.Serialize(receipt));
        var backup=new VerifiedRawBackup(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId);
        Assert.Equal(receipt,ControlledRecoveryEvidence.RequireBoundary(fixture.Path,backup));
        byte[] corrupt=plan.ExpectedPartialImage();corrupt[0xC0091]=0;File.WriteAllBytes(Path.Combine(fixture.Path,"partial.read"),corrupt);
        Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireBoundary(fixture.Path,backup));
    }
    [Fact] public void PartialEvidenceCannotQualifyOriginalRestorationOrAnActivePowerCut()
    {
        using var fixture=new EvidenceFolder();var plan=Plan(ControlledRecoveryStage.Prepare);var backup=new VerifiedRawBackup(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId);
        File.WriteAllText(Path.Combine(fixture.Path,"boundary.json"),JsonSerializer.Serialize(new ControlledBoundaryReceipt(1,plan.Original.OriginalSha256,RomStorage.Hash(plan.ExpectedPartialImage()),OriginalRestoreKernel.ImageSha256,1,true,true)));
        Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireBoundary(fixture.Path,backup));
        File.WriteAllText(Path.Combine(fixture.Path,"events.jsonl"),"{}");Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireOriginalRestoration(fixture.Path,backup));
    }
    [Fact] public void OriginalRestorationPrerequisiteChecksBothCapturesAndTheCompleteHashChain()
    {
        using var fixture=new EvidenceFolder();fixture.WriteOriginalPrerequisite();var backup=new VerifiedRawBackup(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId);
        ControlledRecoveryEvidence.RequireOriginalRestoration(fixture.Path,backup);
        string path=Path.Combine(fixture.Path,"transaction.jsonl");string text=File.ReadAllText(path);File.WriteAllText(path,text.Replace("erase-verified","erase-intent"));
        Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireOriginalRestoration(fixture.Path,backup));File.WriteAllText(path,text);
        byte[] changed=backup.Data.ToArray();changed[0xE0000]^=1;File.WriteAllBytes(Path.Combine(fixture.Path,"final-pass-2.read"),changed);
        Assert.Throws<InvalidDataException>(()=>ControlledRecoveryEvidence.RequireOriginalRestoration(fixture.Path,backup));
    }
    [Fact] public void MissingEvidenceAndExternalOverridesCannotOpenControlledRecoveryHardware()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),System.IO.Path.GetTempPath(),originalRestore:(_,_)=>{opens++;throw new Exception();});
        Assert.Equal(1,app.Execute(["controlled-recovery-check","missing/original.rom","missing/run"]));
        Assert.Equal(1,app.Execute(["controlled-recovery-resume","missing/original.rom","missing/run","missing/boundary"]));
        Assert.Equal(1,app.Execute(["controlled-recovery-check","original.rom","run","unit-override"]));
        Assert.Equal(1,app.Execute(["controlled-recovery-resume","original.rom","run","boundary","external.bin"]));Assert.Equal(0,opens);
    }
    private sealed class EvidenceFolder:IDisposable
    {
        public string Path { get; }=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        public EvidenceFolder(){Directory.CreateDirectory(Path);Directory.CreateDirectory(System.IO.Path.Combine(Path,"prepare"));}
        public void WriteOriginalPrerequisite()
        {
            var bytes=RestoreQualificationTests.Image();string hash=RomStorage.Hash(bytes);
            var rows=Enumerable.Range(1,115).Select(i=>new {schema=12,sequence=i,version="0.9.0-alpha.15+65153f01b2b1777a0f2799430fd8b451991a5a50",targetProfile=Zyj2RecoveryPolicy.SoftwareId,
                backupSha256=hash,kernelSha256=OriginalRestoreKernel.ImageSha256,kernelBytes=OriginalRestoreKernel.ImageLength,recovery=false,modifiedFlashingEnabled=false,
                stage=i==1?"intact-normal-preflight-passed-before-first-erase-test":i==114?"normal-restart-confirmed-after-original-restoration":i==115?"original-restoration-complete-interrupted-recovery-not-qualified":"fixture-progress",
                result=new{commands=10243,originalUnits=1024,finalMonitorPassed=true,originalSha256=hash,finalRead1Sha256=hash,finalRead2Sha256=hash},
                transport=new{sentSteps=739420,commandsCompleted=10243,originalUnitsVerified=1024,failed=false,awaitingReply=false},lastResult=new{opcode=127,sequence=10243,index=0,nextUnit=1024,flags=99,status=0,flashResult=0,fccs=0}});
            File.WriteAllLines(System.IO.Path.Combine(Path,"events.jsonl"),rows.Select(r=>JsonSerializer.Serialize(r)));
            foreach(string name in new[]{"final-pass-1.read","final-pass-2.read"})File.WriteAllBytes(System.IO.Path.Combine(Path,name),bytes);
            var plan=RestoreQualificationTests.Plan();using var journal=new RestoreJournal(System.IO.Path.Combine(Path,"transaction.jsonl"),plan.Original);
            journal.Commit("started");journal.Commit("bound");journal.Commit("erase-intent");journal.Commit("erase-verified");
            for(int i=0;i<1024;i++){journal.Commit("unit-intent",i);journal.Commit("unit-verified",i);}journal.Commit("full-readback-1");journal.Commit("complete");
        }
        public void Dispose()=>Directory.Delete(Path,true);
    }
}
