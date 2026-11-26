using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Consistency with the completed alpha.16 cold-recovery run, never ECU authentication.</summary>
public static class Eb15Prerequisite
{
    private static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    public static void Require(string folder,VerifiedRawBackup backup)
    {
        try { Validate(folder,backup); }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {throw new InvalidDataException("EB15 prerequisite evidence is malformed or incomplete. No adapter was opened.",e);}
    }
    private static void Validate(string folder,VerifiedRawBackup backup)
    {
        var boundary=ControlledRecoveryEvidence.RequireBoundary(folder,backup);
        string hash=RomStorage.Hash(backup.Data),path=Path.Combine(folder,"events.jsonl");
        Require(new FileInfo(path).Length is >0 and <=2_000_000,"Choose the completed alpha.16 controlled-recovery run folder.");
        var lines=File.ReadAllLines(path);Require(lines.Length==174,"The paired alpha.16 cold-recovery run must be complete.");
        var stages=new List<string>();JsonElement last=default;bool checkedPartial=false,finishedPrepare=false,finishedRecovery=false;
        for(int i=0;i<lines.Length;i++)
        {
            using var doc=JsonDocument.Parse(lines[i]);var x=doc.RootElement;
            Require(x.GetProperty("schema").GetInt32()==13 && x.GetProperty("sequence").GetInt32()==i+1 &&
                x.GetProperty("version").GetString()=="0.9.0-alpha.16+03afa7894edbe17df0b5931b4faf53987290f288" &&
                x.GetProperty("targetProfile").GetString()==Zyj2RecoveryPolicy.SoftwareId && x.GetProperty("backupSha256").GetString()==hash &&
                x.GetProperty("kernelSha256").GetString()==OriginalRestoreKernel.ImageSha256 && x.GetProperty("kernelBytes").GetInt32()==3012 &&
                !x.GetProperty("activeOperationPowerCutImplemented").GetBoolean() && !x.GetProperty("activeOperationPowerLossQualified").GetBoolean() &&
                !x.GetProperty("modifiedFlashingEnabled").GetBoolean(),"EB15 prerequisite does not match the reviewed alpha.16 run and original.");
            foreach(string name in new[]{"transport","prepareTransport","recoverTransport"})
            {var t=x.GetProperty(name);if(t.ValueKind!=JsonValueKind.Null)Require(!t.GetProperty("failed").GetBoolean()&&!t.GetProperty("awaitingReply").GetBoolean(),"Incomplete transport cannot qualify EB15.");}
            string stage=x.GetProperty("stage").GetString()!;stages.Add(stage);
            if(stage=="completed-unit-zero-partial-image-and-final-monitor-passed-no-active-operation")finishedPrepare=Result(x,6149,1,298431);
            if(stage=="cold-entry-full-partial-image-confirmed-before-recovery-erase")
            {
                checkedPartial=x.GetProperty("partialConfirmedAfterColdEntry").GetBoolean() && !x.GetProperty("transport").GetProperty("eraseAttempted").GetBoolean();
            }
            if(stage=="original-block-restore-dual-readback-and-final-monitor-passed")finishedRecovery=Result(x,10243,1024,739420);
            last=x.Clone();
        }
        Require(stages[58]=="completed-unit-zero-partial-image-and-final-monitor-passed-no-active-operation" && stages[83]=="cold-entry-full-partial-image-confirmed-before-recovery-erase" && stages[167]=="original-block-restore-dual-readback-and-final-monitor-passed" && finishedPrepare&&checkedPartial&&finishedRecovery&&stages[^1]=="controlled-completed-unit-recovery-passed-active-power-loss-not-qualified" &&
            stages.IndexOf("boundary-ready-before-operator-power-cycle") is >0 &&
            stages.IndexOf("boundary-ready-before-operator-power-cycle")<stages.IndexOf("cold-entry-full-partial-image-confirmed-before-recovery-erase") &&
            stages.Contains("normal-restart-confirmed-after-controlled-cold-recovery") && last.GetProperty("boundarySaved").GetBoolean() &&
            last.GetProperty("partialConfirmedAfterColdEntry").GetBoolean(),"A completed paired cold recovery and normal restart are required before EB15 qualification.");
        var prepared=last.GetProperty("prepared");
        Require(prepared.GetProperty("commands").GetInt32()==6149&&prepared.GetProperty("originalUnits").GetInt32()==1&&
            prepared.GetProperty("originalSha256").GetString()==hash&&prepared.GetProperty("partialSha256").GetString()==boundary.PartialSha256&&
            prepared.GetProperty("calibrationIdErased").GetBoolean()&&prepared.GetProperty("finalMonitorPassed").GetBoolean()&&
            Transport(last.GetProperty("prepareTransport"),6149,1,298431)&&Transport(last.GetProperty("recoverTransport"),10243,1024,739420),"Final boundary/recovery transport evidence is incomplete.");
        var restored=last.GetProperty("restored");
        Require(restored.GetProperty("commands").GetInt32()==10243&&restored.GetProperty("originalUnits").GetInt32()==1024&&restored.GetProperty("finalMonitorPassed").GetBoolean()&&
            restored.GetProperty("originalSha256").GetString()==hash&&restored.GetProperty("finalRead1Sha256").GetString()==hash&&restored.GetProperty("finalRead2Sha256").GetString()==hash,"Original restoration result is incomplete.");
        foreach(string name in new[]{"final-pass-1.read","final-pass-2.read"})
        {string p=Path.Combine(folder,name);Require(new FileInfo(p).Length==TargetPolicy.RomSize&&File.ReadAllBytes(p).AsSpan().SequenceEqual(backup.Data),"Both original-matching final captures are required.");}
        ControlledRecoveryEvidence.RequireJournal(Path.Combine(folder,"recover","transaction.jsonl"),hash,false);
    }
    private static bool Result(JsonElement x,int commands,int units,int steps)
    {
        var q=x.GetProperty("lastResult");var t=x.GetProperty("transport");
        return q.GetProperty("opcode").GetInt32()==127&&q.GetProperty("sequence").GetInt32()==commands&&q.GetProperty("index").GetInt32()==0&&
            q.GetProperty("status").GetInt32()==0&&q.GetProperty("flashResult").GetUInt32()==0&&q.GetProperty("flags").GetInt32()==99&&
            q.GetProperty("nextUnit").GetInt32()==units&&q.GetProperty("fccs").GetInt32()==0&&Transport(t,commands,units,steps);
    }
    private static bool Transport(JsonElement t,int commands,int units,int steps)
    {
        return t.GetProperty("sentSteps").GetInt32()==steps&&t.GetProperty("bootBytes").GetInt32()==28928&&
            t.GetProperty("kernelBytes").GetInt32()==3012&&t.GetProperty("stateBytes").GetInt32()==48&&
            t.GetProperty("commandsCompleted").GetInt32()==commands&&t.GetProperty("originalUnitsVerified").GetInt32()==units&&
            !t.GetProperty("failed").GetBoolean()&&!t.GetProperty("awaitingReply").GetBoolean();
    }
}
