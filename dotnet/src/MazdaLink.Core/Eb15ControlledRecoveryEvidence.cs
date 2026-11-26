using System.Text.Json;

namespace MazdaLink.Core;

public sealed record Eb15ControlledBoundaryReceipt(int Schema,int EraseBlock,string OriginalSha256,string PartialSha256,string KernelSha256,
    int UnitsVerified,bool BootValidityPointerErased,bool FinalMonitorPassed,bool ActiveOperationInterrupted,
    string OriginalRestorationEventSha256,string OriginalRestorationJournalSha256);

/// <summary>Fixed EB15 evidence consistency checks; never authentication or unique ECU identity.</summary>
public static class Eb15ControlledRecoveryEvidence
{
    private static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    private static void Validate(Action action)
    {
        try{action();}catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {throw new InvalidDataException("EB15 controlled-recovery evidence is malformed or incomplete. No adapter was opened.",e);}
    }
    private static void Image(string path,byte[] expected)
    {Require(new FileInfo(path).Length==TargetPolicy.RomSize&&File.ReadAllBytes(path).AsSpan().SequenceEqual(expected),"A complete exact verification capture is required.");}
    public static void RequireOriginalRestoration(string folder,VerifiedRawBackup backup)=>Validate(()=>
    {
        string hash=RomStorage.Hash(backup.Data),path=Path.Combine(folder,"events.jsonl");
        Require(new FileInfo(path).Length is >0 and <=2_000_000,"Choose the completed alpha.18 original EB15 restoration folder.");
        string[] lines=File.ReadAllLines(path);Require(lines.Length==115,"The first original EB15 restoration and normal restart must be complete.");
        var stages=new List<string>();JsonElement last=default;bool erasePassed=false;
        for(int i=0;i<lines.Length;i++)
        {
            using var doc=JsonDocument.Parse(lines[i]);var x=doc.RootElement;
            Require(x.GetProperty("schema").GetInt32()==14&&x.GetProperty("sequence").GetInt32()==i+1&&
                x.GetProperty("version").GetString()=="0.9.0-alpha.18+c238847d4b0adab62a0143c4146ba84c8f75037e"&&
                x.GetProperty("targetProfile").GetString()==Zyj2RecoveryPolicy.SoftwareId&&x.GetProperty("backupSha256").GetString()==hash&&
                x.GetProperty("eraseBlock").GetInt32()==15&&x.GetProperty("blockStart").GetInt32()==0xE0000&&
                x.GetProperty("blockSha256").GetString()==RomStorage.Hash(backup.Data.AsSpan(0xE0000))&&
                x.GetProperty("expectedBootSha256").GetString()==RomStorage.Hash(backup.Data.AsSpan(0,0x7100))&&
                x.GetProperty("kernelSha256").GetString()==Eb15OriginalRestoreKernel.ImageSha256&&x.GetProperty("kernelBytes").GetInt32()==3012&&
                !x.GetProperty("recovery").GetBoolean()&&x.GetProperty("normalDiagnosticsRequiredBeforeEntry").GetBoolean()&&
                x.GetProperty("originalOnlyEraseProgrammingImplemented").GetBoolean()&&!x.GetProperty("modifiedFlashingEnabled").GetBoolean()&&
                !x.GetProperty("interruptedWriteRecoveryQualified").GetBoolean(),"EB15 restoration evidence does not match the reviewed first test and this original.");
            var t=x.GetProperty("transport");if(t.ValueKind!=JsonValueKind.Null)Require(!t.GetProperty("failed").GetBoolean()&&!t.GetProperty("awaitingReply").GetBoolean(),"Failed or pending restoration evidence cannot qualify recovery.");
            var q=x.GetProperty("lastResult");if(q.ValueKind!=JsonValueKind.Null)Require(q.GetProperty("status").GetInt32()==0&&q.GetProperty("flashResult").GetUInt32()==0&&q.GetProperty("fccs").GetInt32()==0,"A recorded controller/cleanup result failed.");
            string stage=x.GetProperty("stage").GetString()!;stages.Add(stage);
            if(stage=="erase-result-and-busy-callback-observed-before-full-blank-preserved-readback")erasePassed=Reply(q,33,2050,15,0);
            last=x.Clone();
        }
        Require(erasePassed&&stages[^1]=="eb15-original-restoration-complete-cold-and-active-recovery-not-qualified"&&
            stages.Contains("BeforeErase-blocks-verified: 2048")&&stages.Contains("intact-normal-preflight-passed-before-first-erase-test")&&stages.Contains("normal-restart-confirmed-after-original-restoration")&&
            stages.IndexOf("BeforeErase-blocks-verified: 2048")<stages.IndexOf("erase-result-and-busy-callback-observed-before-full-blank-preserved-readback")&&
            stages.Contains("EraseReadback-blocks-verified: 2048")&&stages.Contains("original-units-verified: 1024"),"Completed original EB15 restoration and normal restart are required.");
        var result=last.GetProperty("result");var transport=last.GetProperty("transport");
        Require(result.GetProperty("commands").GetInt32()==10243&&result.GetProperty("originalUnits").GetInt32()==1024&&result.GetProperty("finalMonitorPassed").GetBoolean()&&
            result.GetProperty("originalSha256").GetString()==hash&&result.GetProperty("finalRead1Sha256").GetString()==hash&&result.GetProperty("finalRead2Sha256").GetString()==hash&&
            transport.GetProperty("sentSteps").GetInt32()==739420&&transport.GetProperty("commandsCompleted").GetInt32()==10243&&transport.GetProperty("originalUnitsVerified").GetInt32()==1024&&
            transport.GetProperty("bootBytes").GetInt32()==28928&&transport.GetProperty("kernelBytes").GetInt32()==3012&&transport.GetProperty("stateBytes").GetInt32()==48&&
            Reply(last.GetProperty("lastResult"),127,10243,0,1024),"The original EB15 results are incomplete.");
        foreach(string name in new[]{"final-pass-1.read","final-pass-2.read"})Image(Path.Combine(folder,name),backup.Data);
        ControlledRecoveryEvidence.RequireEb15Journal(Path.Combine(folder,"transaction.jsonl"),hash,false);
    });
    private static bool Reply(JsonElement q,int opcode,int sequence,int index,int next)=>
        q.GetProperty("opcode").GetInt32()==opcode&&q.GetProperty("sequence").GetInt32()==sequence&&q.GetProperty("index").GetInt32()==index&&
        q.GetProperty("status").GetInt32()==0&&q.GetProperty("flashResult").GetUInt32()==0&&q.GetProperty("flags").GetInt32()==99&&
        q.GetProperty("nextUnit").GetInt32()==next&&q.GetProperty("fccs").GetInt32()==0;
    public static byte[] ExpectedPartial(byte[] original)
    {byte[] bytes=original.ToArray();bytes.AsSpan(0xE0080,0x1FF80).Fill(255);return bytes;}
    public static Eb15ControlledBoundaryReceipt RequireBoundary(string folder,VerifiedRawBackup backup,string originalRestorationFolder)
    {
        Eb15ControlledBoundaryReceipt? receipt=null;
        Validate(()=>
        {
            RequireOriginalRestoration(originalRestorationFolder,backup);
            string path=Path.Combine(folder,"boundary.json");Require(new FileInfo(path).Length is >0 and <4096,"Missing or invalid EB15 boundary receipt.");
            receipt=JsonSerializer.Deserialize<Eb15ControlledBoundaryReceipt>(File.ReadAllText(path))??throw new InvalidDataException("Missing EB15 receipt.");
            byte[] expected=ExpectedPartial(backup.Data);
            Require(receipt.Schema==1&&receipt.EraseBlock==15&&receipt.OriginalSha256==RomStorage.Hash(backup.Data)&&receipt.PartialSha256==RomStorage.Hash(expected)&&
                receipt.KernelSha256==Eb15OriginalRestoreKernel.ImageSha256&&receipt.UnitsVerified==1&&receipt.BootValidityPointerErased&&receipt.FinalMonitorPassed&&!receipt.ActiveOperationInterrupted&&
                receipt.OriginalRestorationEventSha256==RomStorage.Hash(File.ReadAllBytes(Path.Combine(originalRestorationFolder,"events.jsonl")))&&
                receipt.OriginalRestorationJournalSha256==RomStorage.Hash(File.ReadAllBytes(Path.Combine(originalRestorationFolder,"transaction.jsonl"))),"This is not the fixed completed EB15 unit-zero boundary bound to this restoration.");
            Image(Path.Combine(folder,"partial.read"),expected);
            ControlledRecoveryEvidence.RequireEb15Journal(Path.Combine(folder,"prepare","transaction.jsonl"),receipt.OriginalSha256,true);
        });return receipt!;
    }
}
