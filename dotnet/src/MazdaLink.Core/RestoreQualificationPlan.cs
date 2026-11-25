using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace MazdaLink.Core;

public enum RestorePhase { Initialize, BeforeErase, Erase, EraseReadback, Program, UnitReadback, FinalReadback1, FinalReadback2, Finish }
public sealed record RestoreCommand(TransactionCommand Command,RestorePhase Phase);

/// <summary>Consistent prior read/stage evidence; not authentication or unique ECU identification.</summary>
public static class RestorePrerequisite
{
    public static void Require(string evidencePath,VerifiedRawBackup backup)
    {
        try{RequireFields(evidencePath,backup);}
        catch(Exception error) when(error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {throw new InvalidDataException("Prerequisite evidence is malformed or incomplete. No adapter was opened.",error);}
    }
    private static void RequireFields(string evidencePath,VerifiedRawBackup backup)
    {
        var info=new FileInfo(evidencePath);
        if(info.Length is <=0 or >2_000_000)throw new InvalidDataException("Choose the completed alpha.14 native-transaction events.jsonl.");
        var lines=File.ReadAllLines(evidencePath);
        if(lines.Length!=102)throw new InvalidDataException("The prerequisite native read/staging run is incomplete.");
        JsonElement last=default;int sequence=0;var stages=new List<string>();var replies=new List<JsonElement>();
        string originalHash=RomStorage.Hash(backup.Data),bootHash=RomStorage.Hash(backup.Data.AsSpan(0,0x7100));
        foreach(string line in lines)
        {
            using var doc=JsonDocument.Parse(line);var r=doc.RootElement;
            if(r.GetProperty("schema").GetInt32()!=11 || r.GetProperty("sequence").GetInt32()!=++sequence ||
                r.GetProperty("version").GetString()!="0.9.0-alpha.14+9aa5a027614c7b4aafd5e8c7d42fbb615854a790" ||
                r.GetProperty("targetProfile").GetString()!=Zyj2RecoveryPolicy.SoftwareId ||
                r.GetProperty("backupSha256").GetString()!=originalHash ||
                r.GetProperty("expectedBootSha256").GetString()!=bootHash ||
                r.GetProperty("kernelBytes").GetInt32()!=2036 ||
                r.GetProperty("kernelSha256").GetString()!=NativeTransactionPolicy.ImageSha256 ||
                r.GetProperty("hardwareFlashEnabled").GetBoolean())throw new InvalidDataException("Prerequisite evidence does not match this original backup and the reviewed alpha.14 trial.");
            if(r.GetProperty("romEraseOrProgrammingImplemented").GetBoolean() || r.GetProperty("actualFlashBusyTested").GetBoolean() || r.GetProperty("interruptedWriteRecoveryTested").GetBoolean())throw new InvalidDataException("Unexpected prerequisite scope.");
            string stage=r.GetProperty("stage").GetString()!;stages.Add(stage);
            if(stage=="native-command-result")replies.Add(r.GetProperty("lastReply").Clone());
            last=r.Clone();
        }
        if(!stages.Contains("normal-restart-confirmed-after-native-trial") || replies.Count!=10 ||
            !stages.Contains("complete-current-rom-matched-original-before-ram-staging"))throw new InvalidDataException("Prerequisite stages are incomplete.");
        int[] units=[0,1,127,255,511,767,1022,1023];
        for(int i=0;i<10;i++)
        {
            var reply=replies[i];int opcode=i==0?0:i==9?127:18,seq=i==0?1:i==9?2058:2049+i,unit=i is >0 and <9?units[i-1]:0;
            uint crc=i is >0 and <9?TransactionCrc32.Calculate(backup.Data.AsSpan(0xC0000+unit*128,128)):0;
            if(reply.GetProperty("opcode").GetInt32()!=opcode||reply.GetProperty("sequence").GetInt32()!=seq||reply.GetProperty("index").GetInt32()!=unit||
                reply.GetProperty("status").GetInt32()!=0||reply.GetProperty("flashResult").GetUInt32()!=0||reply.GetProperty("flags").GetInt32()!=1||
                reply.GetProperty("nextUnit").GetInt32()!=0||reply.GetProperty("fccs").GetInt32()!=0||reply.GetProperty("dataCrc32").GetUInt32()!=crc)
                throw new InvalidDataException("Prerequisite command/staging results do not match this backup.");
        }
        var result=last.GetProperty("result");var transport=last.GetProperty("transport");
        if(last.GetProperty("stage").GetString()!="complete-no-flash-recovery-qualification" ||
            result.GetProperty("readBytes").GetInt32()!=TargetPolicy.RomSize || !result.GetProperty("matchesOriginal").GetBoolean() ||
            result.GetProperty("readbackSha256").GetString()!=RomStorage.Hash(backup.Data) ||
            result.GetProperty("stagedUnits").GetInt32()!=8 || result.GetProperty("commands").GetInt32()!=2058 ||
            result.GetProperty("bootBytes").GetInt32()!=28928 || result.GetProperty("kernelBytes").GetInt32()!=2036 ||
            transport.GetProperty("failed").GetBoolean() || transport.GetProperty("awaitingReply").GetBoolean() ||
            transport.GetProperty("commandsCompleted").GetInt32()!=2058 || transport.GetProperty("sentSteps").GetInt32()!=142812 ||
            transport.GetProperty("bootBytes").GetInt32()!=28928 || transport.GetProperty("kernelBytes").GetInt32()!=2036 || transport.GetProperty("stateBytes").GetInt32()!=48)
            throw new InvalidDataException("A completed read/staging trial and normal restart are required before this qualification test.");
    }
}

/// <summary>Fixed original-only EB14 script. Recovery always cold-binds and re-erases;
/// no external code, address, modified data or resume index is accepted.</summary>
public sealed class RestoreQualificationPlan:IReadOnlyList<TransactionWireStep>
{
    private readonly List<TransactionWireStep> installation=[];
    private readonly int[] starts;
    private readonly byte[] original;
    private int cached=-1;
    private List<TransactionWireStep> commandSteps=[];
    public OriginalBlockRestorePlan Original { get; }
    public RecoveryBindingPlan Binding { get; }
    public bool Recovery { get; }
    public IReadOnlyList<RestoreCommand> Commands { get; }
    public int InstallationEnd=>Binding.Count+installation.Count;
    public int Count { get; }
    private RestoreQualificationPlan(byte[] bytes,RecoveryBindingPlan binding,OriginalBlockRestorePlan originalPlan,bool recovery,uint cookie)
    {
        original=bytes.ToArray();Binding=binding;Original=originalPlan;Recovery=recovery;
        TransactionTrialPlan.AppendBlob(installation,NativeTransactionPolicy.KernelAddress,OriginalRestoreKernel.Image,"kernel",null);
        TransactionTrialPlan.AppendBlob(installation,NativeTransactionPolicy.StateAddress,new byte[48],"state",null);
        ushort seq=1;var commands=new List<RestoreCommand>();
        void Add(TransactionOpcode op,RestorePhase phase,ushort index=0,byte[]? staging=null)=>commands.Add(new(new(op,seq++,cookie,index,staging),phase));
        void ReadAll(RestorePhase phase){for(ushort i=0;i<2048;i++)Add(TransactionOpcode.ReadBlock,phase,i);}
        Add(TransactionOpcode.Initialize,RestorePhase.Initialize);ReadAll(RestorePhase.BeforeErase);
        Add(TransactionOpcode.Erase,RestorePhase.Erase,14);ReadAll(RestorePhase.EraseReadback);
        for(ushort i=0;i<1024;i++){Add(TransactionOpcode.Program,RestorePhase.Program,i,Original.Data(i));Add(TransactionOpcode.VerifyOriginalUnit,RestorePhase.UnitReadback,i);}
        ReadAll(RestorePhase.FinalReadback1);ReadAll(RestorePhase.FinalReadback2);Add(TransactionOpcode.Finish,RestorePhase.Finish);
        Commands=commands.AsReadOnly();starts=new int[commands.Count];int offset=InstallationEnd;
        for(int i=0;i<commands.Count;i++){starts[i]=offset;offset+=38+(commands[i].Command.Opcode==TransactionOpcode.Program?279:0);}
        Count=offset+1;
    }
    public static RestoreQualificationPlan Create(VerifiedRawBackup backup,string prerequisiteEvidence,bool recovery)
    {
        var stable=new VerifiedRawBackup(backup.Data.ToArray(),backup.ReportedCalibration);
        Zyj2RecoveryPolicy.RequireBackup(stable);RestorePrerequisite.Require(prerequisiteEvidence,stable);
        uint cookie;do{cookie=BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));}while(cookie==0);
        return new(stable.Data,RecoveryBindingPlan.Create(stable),OriginalBlockRestorePlan.Create(stable,stable.Data),recovery,cookie);
    }
    public RestoreCommand? CommandFor(int index)
    {
        if(index<InstallationEnd || index==Count-1)return null;
        int i=Array.BinarySearch(starts,index);if(i<0)i=~i-1;return Commands[i];
    }
    public TransactionWireStep this[int index]
    {
        get
        {
            if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));
            if(index<Binding.Count)
            {
                var s=Binding[index];byte[] expected=s.Operation==RamSelfTestOperation.Address?s.Payload[..6]:s.Operation==RamSelfTestOperation.VerifyByte?[0x7A,0xB1,original[s.ImageOffset]]:Zyj2RecoveryPolicy.ExpectedIdentity;
                return new(s.Payload,expected,boot:s.Operation==RamSelfTestOperation.VerifyByte);
            }
            if(index<InstallationEnd)return installation[index-Binding.Count];
            if(index==Count-1)return new(Zyj2RecoveryPolicy.IdentityRequest,Zyj2RecoveryPolicy.ExpectedIdentity);
            int i=Array.BinarySearch(starts,index);if(i<0)i=~i-1;
            if(cached!=i)
            {
                var cmd=Commands[i].Command;var list=new List<TransactionWireStep>();
                if(cmd.Staging is byte[] blob)TransactionTrialPlan.AppendBlob(list,NativeTransactionPolicy.BufferAddress,blob,"staging",cmd);
                TransactionTrialPlan.AppendBlob(list,NativeTransactionPolicy.CommandAddress,cmd.Descriptor(),"command",cmd);
                list.Add(TransactionTrialPlan.Address(NativeTransactionPolicy.KernelAddress,cmd));list.Add(new([0x7A,0xA0,0,0,0,0,0,0],[],TransactionReplyKind.Command,cmd));
                commandSteps=list;cached=i;
            }
            return commandSteps[index-starts[i]];
        }
    }
    internal void RequireData(RestoreCommand entry,TransactionReply reply)
    {
        var cmd=entry.Command;int address=cmd.Index*512;
        if(entry.Phase==RestorePhase.UnitReadback)
        {if(!reply.Data.AsSpan().SequenceEqual(Original.Data(cmd.Index)))throw new InvalidDataException("Original unit readback differs; no request was resent.");return;}
        if(cmd.Opcode!=TransactionOpcode.ReadBlock)return;
        bool inBlock=address>=0xC0000 && address<0xE0000;
        if(entry.Phase==RestorePhase.BeforeErase && Recovery && inBlock)return;
        if(entry.Phase==RestorePhase.EraseReadback && inBlock)
        {if(reply.Data.Any(b=>b!=255))throw new InvalidDataException("Complete EB14 blank verification failed.");return;}
        if(!reply.Data.AsSpan().SequenceEqual(original.AsSpan(address,512)))throw new InvalidDataException("Full image or preserved-region mismatch. No later operation is permitted.");
    }
    public IEnumerator<TransactionWireStep> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
}
