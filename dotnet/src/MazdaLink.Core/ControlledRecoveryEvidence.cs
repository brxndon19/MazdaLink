using System.Text;
using System.Text.Json;

namespace MazdaLink.Core;

public sealed record ControlledBoundaryReceipt(int Schema,string OriginalSha256,string PartialSha256,string KernelSha256,int UnitsVerified,bool FinalMonitorPassed,bool ActiveOperationInterrupted);
/// <summary>Bounded evidence consistency checks, never authentication or unique ECU identity.</summary>
public static class ControlledRecoveryEvidence
{
    private static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    private static string[] Lines(string path,int maximum)
    {
        var file=new FileInfo(path);Require(file.Length is >0 and <=2_000_000,"Evidence file is missing, empty or too large.");
        string[] lines=File.ReadAllLines(path);Require(lines.Length>0&&lines.Length<=maximum,"Evidence record bound exceeded.");return lines;
    }
    private static void Validate(Action check)
    {
        try{check();}catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {throw new InvalidDataException("Recovery prerequisite evidence is malformed or incomplete. No adapter was opened.",e);}
    }
    public static void RequireOriginalRestoration(string folder,VerifiedRawBackup backup)=>Validate(()=>
    {
        string hash=RomStorage.Hash(backup.Data);string[] lines=Lines(Path.Combine(folder,"events.jsonl"),256);Require(lines.Length==115,"Choose the completed alpha.15 original-restoration folder.");
        var stages=new List<string>();JsonElement last=default;
        for(int i=0;i<lines.Length;i++)
        {
            using var doc=JsonDocument.Parse(lines[i]);var r=doc.RootElement;
            Require(r.GetProperty("schema").GetInt32()==12 && r.GetProperty("sequence").GetInt32()==i+1 &&
                r.GetProperty("version").GetString()=="0.9.0-alpha.15+65153f01b2b1777a0f2799430fd8b451991a5a50" &&
                r.GetProperty("targetProfile").GetString()==Zyj2RecoveryPolicy.SoftwareId && r.GetProperty("backupSha256").GetString()==hash &&
                r.GetProperty("kernelSha256").GetString()==OriginalRestoreKernel.ImageSha256 && r.GetProperty("kernelBytes").GetInt32()==OriginalRestoreKernel.ImageLength &&
                !r.GetProperty("recovery").GetBoolean() && !r.GetProperty("modifiedFlashingEnabled").GetBoolean(),"Restoration evidence does not match this original and the reviewed alpha.15 test.");
            stages.Add(r.GetProperty("stage").GetString()!);last=r.Clone();
        }
        Require(stages[^1]=="original-restoration-complete-interrupted-recovery-not-qualified" && stages.Contains("normal-restart-confirmed-after-original-restoration") && stages.Contains("intact-normal-preflight-passed-before-first-erase-test"),"Successful original restoration and normal restart evidence are required.");
        var result=last.GetProperty("result");var transport=last.GetProperty("transport");var reply=last.GetProperty("lastResult");
        Require(result.GetProperty("commands").GetInt32()==10243 && result.GetProperty("originalUnits").GetInt32()==1024 && result.GetProperty("finalMonitorPassed").GetBoolean() &&
            result.GetProperty("originalSha256").GetString()==hash && result.GetProperty("finalRead1Sha256").GetString()==hash && result.GetProperty("finalRead2Sha256").GetString()==hash &&
            transport.GetProperty("sentSteps").GetInt32()==739420 && transport.GetProperty("commandsCompleted").GetInt32()==10243 && transport.GetProperty("originalUnitsVerified").GetInt32()==1024 &&
            !transport.GetProperty("failed").GetBoolean() && !transport.GetProperty("awaitingReply").GetBoolean() && reply.GetProperty("flags").GetInt32()==99 &&
            reply.GetProperty("opcode").GetInt32()==127 && reply.GetProperty("sequence").GetInt32()==10243 && reply.GetProperty("index").GetInt32()==0 && reply.GetProperty("nextUnit").GetInt32()==1024 &&
            reply.GetProperty("status").GetInt32()==0 && reply.GetProperty("flashResult").GetUInt32()==0 && reply.GetProperty("fccs").GetInt32()==0,"Original restoration results are incomplete or unsuccessful.");
        foreach(string name in new[]{"final-pass-1.read","final-pass-2.read"})RequireImage(Path.Combine(folder,name),backup.Data);
        RequireJournal(Path.Combine(folder,"transaction.jsonl"),hash,false);
    });
    public static void RequireJournal(string path,string originalHash,bool partial)=>RequireJournalBlock(path,originalHash,partial,false);
    internal static void RequireEb15Journal(string path,string originalHash,bool partial)=>RequireJournalBlock(path,originalHash,partial,true);
    private static void RequireJournalBlock(string path,string originalHash,bool partial,bool eb15)=>Validate(()=>
    {
        string[] lines=Lines(path,2060);var expected=new List<(string,int)>{("started",-1),("bound",-1),("erase-intent",-1),("erase-verified",-1)};
        for(int i=0;i<(partial?1:1024);i++){expected.Add(("unit-intent",i));expected.Add(("unit-verified",i));}
        expected.Add((partial?"partial-readback":"full-readback-1",-1));expected.Add((partial?"controlled-stop":"complete",-1));
        Require(lines.Length==expected.Count,"Transaction journal is incomplete or has an unexpected operation sequence.");
        string previous=new('0',64);string? session=null;
        for(int i=0;i<lines.Length;i++)
        {
            var entry=JsonSerializer.Deserialize<RestoreJournalEntry>(lines[i])??throw new InvalidDataException("Missing journal entry.");session??=entry.Session;
            Require(entry.Schema==(eb15?2:1)&&(eb15?entry.Block==15:entry.Block is null)&&Guid.TryParseExact(entry.Session,"N",out _)&&entry.Session==session&&entry.Sequence==i+1&&entry.PreviousHash==previous&&entry.OriginalSha256==originalHash&&
                (entry.Stage,entry.Unit)==expected[i],"Transaction journal binding, hash chain or intent/verification order failed.");
            previous=RomStorage.Hash(Encoding.UTF8.GetBytes(lines[i]));
        }
    });
    public static byte[] ExpectedPartial(byte[] original)
    {byte[] data=original.ToArray();data.AsSpan(0xC0080,0x1FF80).Fill(255);return data;}
    private static void RequireImage(string path,byte[] expected)
    {Require(new FileInfo(path).Length==TargetPolicy.RomSize,"A complete verification capture is required.");Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(expected),"Verification capture differs from the expected image.");}
    public static ControlledBoundaryReceipt RequireBoundary(string folder,VerifiedRawBackup backup)
    {
        ControlledBoundaryReceipt? receipt=null;
        Validate(()=>
        {
            string path=Path.Combine(folder,"boundary.json");Require(new FileInfo(path).Length is >0 and <4096,"Controlled boundary receipt is missing or invalid.");
            receipt=JsonSerializer.Deserialize<ControlledBoundaryReceipt>(File.ReadAllText(path))??throw new InvalidDataException("Missing boundary receipt.");
            byte[] expected=ExpectedPartial(backup.Data);
            Require(receipt.Schema==1&&receipt.OriginalSha256==RomStorage.Hash(backup.Data)&&receipt.PartialSha256==RomStorage.Hash(expected)&&
                receipt.KernelSha256==OriginalRestoreKernel.ImageSha256&&receipt.UnitsVerified==1&&receipt.FinalMonitorPassed&&!receipt.ActiveOperationInterrupted,"This is not the fixed completed-unit-zero boundary.");
            RequireImage(Path.Combine(folder,"partial.read"),expected);
            RequireJournal(Path.Combine(folder,"prepare","transaction.jsonl"),receipt.OriginalSha256,true);
        });return receipt!;
    }
}
