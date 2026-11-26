using System.Text.Json;
using System.Text.Json.Nodes;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class Eb15PrerequisiteTests
{
    private sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        public VerifiedRawBackup Backup {get;}=new(RestoreQualificationTests.Image(),Zyj2RecoveryPolicy.SoftwareId);
        public Fixture()
        {
            Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"prepare"));Directory.CreateDirectory(Path.Combine(Root,"recover"));
            var p=ControlledRecoveryTests.Plan(ControlledRecoveryStage.Prepare);string hash=RomStorage.Hash(Backup.Data),partialHash=RomStorage.Hash(p.ExpectedPartialImage());
            File.WriteAllBytes(Path.Combine(Root,"partial.read"),p.ExpectedPartialImage());
            File.WriteAllText(Path.Combine(Root,"boundary.json"),JsonSerializer.Serialize(new ControlledBoundaryReceipt(1,hash,RomStorage.Hash(p.ExpectedPartialImage()),OriginalRestoreKernel.ImageSha256,1,true,false)));
            foreach(var (name,count) in new[]{("prepare",1),("recover",1024)})
            {
                using var j=new RestoreJournal(Path.Combine(Root,name,"transaction.jsonl"),p.Original);
                j.Commit("started");j.Commit("bound");j.Commit("erase-intent");j.Commit("erase-verified");
                for(int i=0;i<count;i++){j.Commit("unit-intent",i);j.Commit("unit-verified",i);}
                j.Commit(count==1?"partial-readback":"full-readback-1");j.Commit(count==1?"controlled-stop":"complete");
            }
            foreach(string n in new[]{"final-pass-1.read","final-pass-2.read"})File.WriteAllBytes(Path.Combine(Root,n),Backup.Data);
            object Transport(int c,int u,int s,bool erase=true)=>new{sentSteps=s,bootBytes=28928,kernelBytes=3012,stateBytes=48,commandsCompleted=c,originalUnitsVerified=u,awaitingReply=false,failed=false,eraseAttempted=erase};
            object Result(int c,int u)=>new{opcode=127,sequence=c,index=0,status=0,flashResult=0,flags=99,nextUnit=u,fccs=0};
            var stages=new Dictionary<int,string>{{59,"completed-unit-zero-partial-image-and-final-monitor-passed-no-active-operation"},{61,"boundary-ready-before-operator-power-cycle"},{84,"cold-entry-full-partial-image-confirmed-before-recovery-erase"},{168,"original-block-restore-dual-readback-and-final-monitor-passed"},{173,"normal-restart-confirmed-after-controlled-cold-recovery"},{174,"controlled-completed-unit-recovery-passed-active-power-loss-not-qualified"}};
            var rows=Enumerable.Range(1,174).Select(i=>new{schema=13,sequence=i,version="0.9.0-alpha.16+03afa7894edbe17df0b5931b4faf53987290f288",targetProfile=Zyj2RecoveryPolicy.SoftwareId,backupSha256=hash,
                kernelSha256=OriginalRestoreKernel.ImageSha256,kernelBytes=3012,activeOperationPowerCutImplemented=false,activeOperationPowerLossQualified=false,modifiedFlashingEnabled=false,
                stage=stages.GetValueOrDefault(i,"synthetic-progress"),boundarySaved=i>=60,partialConfirmedAfterColdEntry=i>=84,
                prepareTransport=i==174?Transport(6149,1,298431):null,recoverTransport=i==174?Transport(10243,1024,739420):null,
                transport=i==59?Transport(6149,1,298431):i==168?Transport(10243,1024,739420):i==84?Transport(2049,0,142351,false):null,
                lastResult=i==59?Result(6149,1):i==168?Result(10243,1024):null,
                prepared=new{commands=6149,originalUnits=1,originalSha256=hash,partialSha256=partialHash,calibrationIdErased=true,finalMonitorPassed=true},
                restored=new{commands=10243,originalUnits=1024,originalSha256=hash,finalRead1Sha256=hash,finalRead2Sha256=hash,finalMonitorPassed=true}});
            File.WriteAllLines(Path.Combine(Root,"events.jsonl"),rows.Select(x=>JsonSerializer.Serialize(x)));
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact] public void CompletedPairedRecoveryEvidenceRequiresEveryCaptureAndBothJournalChains()
    {
        using var f=new Fixture();Eb15Prerequisite.Require(f.Root,f.Backup);
        foreach(string name in new[]{"partial.read","final-pass-1.read","final-pass-2.read","prepare/transaction.jsonl","recover/transaction.jsonl","boundary.json"})
        {
            string p=Path.Combine(f.Root,name);byte[] original=File.ReadAllBytes(p);File.WriteAllBytes(p,[]);
            Assert.ThrowsAny<Exception>(()=>Eb15Prerequisite.Require(f.Root,f.Backup));File.WriteAllBytes(p,original);
        }
    }
    [Fact] public void IncompleteWrongSourceFailedTransportAndFalseRecoveryClaimsCannotQualifyEb15()
    {
        using var f=new Fixture();string p=Path.Combine(f.Root,"events.jsonl");string[] original=File.ReadAllLines(p);
        foreach(var (row,key,value) in new (int,string,JsonNode?)[]{(0,"version",JsonValue.Create("0.9.0-alpha.17")),(173,"stage",JsonValue.Create("incomplete")),(83,"partialConfirmedAfterColdEntry",JsonValue.Create(false)),(167,"modifiedFlashingEnabled",JsonValue.Create(true)),(173,"activeOperationPowerLossQualified",JsonValue.Create(true))})
        {
            string[] lines=original.ToArray();var x=JsonNode.Parse(lines[row])!;x[key]=value;lines[row]=x.ToJsonString();File.WriteAllLines(p,lines);
            Assert.Throws<InvalidDataException>(()=>Eb15Prerequisite.Require(f.Root,f.Backup));
        }
        string[] bad=original.ToArray();var failed=JsonNode.Parse(bad[167])!;failed["transport"]!["failed"]=true;bad[167]=failed.ToJsonString();File.WriteAllLines(p,bad);
        Assert.Throws<InvalidDataException>(()=>Eb15Prerequisite.Require(f.Root,f.Backup));
        File.WriteAllLines(p,original[..173]);Assert.Throws<InvalidDataException>(()=>Eb15Prerequisite.Require(f.Root,f.Backup));
    }
}
