using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MazdaLink.Core;

public enum TransactionReplyKind { Echo, Single, Command }
public sealed class TransactionWireStep
{
    private readonly byte[] payload,reply;
    public byte[] Payload=>payload.ToArray();
    public TransactionReplyKind Kind { get; }
    public TransactionCommand? Command { get; }
    public bool BootByte { get; }
    public bool KernelByte { get; }
    public bool StateByte { get; }
    public bool CommandByte { get; }
    public bool StagingByte { get; }
    internal TransactionWireStep(byte[] payload,byte[] reply,TransactionReplyKind kind=TransactionReplyKind.Single,TransactionCommand? command=null,
        bool boot=false,bool kernel=false,bool state=false,bool commandByte=false,bool staging=false)
    {this.payload=payload.ToArray();this.reply=reply.ToArray();Kind=kind;Command=command;BootByte=boot;KernelByte=kernel;StateByte=state;CommandByte=commandByte;StagingByte=staging;}
    public bool Matches(RecoveryFrame frame)=>Kind==TransactionReplyKind.Echo?frame.Status==0x101:
        frame.Id==0x21&&frame.Status==0x100&&frame.Data.Length==8&&frame.Data.AsSpan(0,reply.Length).SequenceEqual(reply);
}
/// <summary>Only the fixed read/staging image, full ROM reads and eight original RAM units. No erase/program command.</summary>
public sealed class TransactionTrialPlan:IReadOnlyList<TransactionWireStep>
{
    public static IReadOnlyList<int> StagingUnits { get; }=Array.AsReadOnly(new[]{0,1,127,255,511,767,1022,1023});
    private readonly byte[] original;
    private readonly List<TransactionWireStep> install=[],commands=[];
    public RecoveryBindingPlan Binding { get; }
    public IReadOnlyList<TransactionCommand> Commands { get; }
    public uint Cookie { get; }
    public int InstallationEnd=>Binding.Count+install.Count;
    public int Count=>InstallationEnd+commands.Count+1;
    private TransactionTrialPlan(byte[] original,RecoveryBindingPlan binding,uint cookie)
    {
        this.original=original.ToArray();Binding=binding;Cookie=cookie;
        AppendBlob(install,NativeTransactionPolicy.KernelAddress,NativeTransactionPolicy.Image,"kernel",null);
        AppendBlob(install,NativeTransactionPolicy.StateAddress,new byte[48],"state",null);
        ushort seq=1;var list=new List<TransactionCommand>{new(TransactionOpcode.Initialize,seq++,cookie)};
        for(ushort i=0;i<2048;i++)list.Add(new(TransactionOpcode.ReadBlock,seq++,cookie,i));
        foreach(int i in StagingUnits)list.Add(new(TransactionOpcode.StageEcho,seq++,cookie,(ushort)i,this.original.AsSpan(0xC0000+i*128,128).ToArray()));
        list.Add(new(TransactionOpcode.Finish,seq,cookie));Commands=list.AsReadOnly();
        foreach(var cmd in list)
        {
            if(cmd.Staging is byte[] blob)AppendBlob(commands,NativeTransactionPolicy.BufferAddress,blob,"staging",cmd);
            AppendBlob(commands,NativeTransactionPolicy.CommandAddress,cmd.Descriptor(),"command",cmd);
            commands.Add(Address(NativeTransactionPolicy.KernelAddress,cmd));
            commands.Add(new([0x7A,0xA0,0,0,0,0,0,0],[],TransactionReplyKind.Command,cmd));
        }
    }
    public static TransactionTrialPlan Create(VerifiedRawBackup backup)
    {
        var stable=new VerifiedRawBackup(backup.Data.ToArray(),backup.ReportedCalibration);Zyj2RecoveryPolicy.RequireBackup(stable);
        uint cookie;do{cookie=BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));}while(cookie==0);
        return new(stable.Data,RecoveryBindingPlan.Create(stable),cookie);
    }
    internal byte[] Original=>original.ToArray();
    internal static TransactionWireStep Address(uint address,TransactionCommand? cmd=null)
    {byte[] p=[0x7A,0x9C,0,0,0,0,0,0];BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(2),address);return new(p,p[..6],command:cmd);}
    internal static void AppendBlob(List<TransactionWireStep> list,uint address,byte[] blob,string category,TransactionCommand? cmd)
    {
        list.Add(Address(address,cmd));
        for(int i=0;i<blob.Length;i+=6)
        {int n=Math.Min(6,blob.Length-i);byte[] p=new byte[8];p[0]=0x7A;p[1]=(byte)(0xA8+n);blob.AsSpan(i,n).CopyTo(p.AsSpan(2));list.Add(new(p,[],TransactionReplyKind.Echo,cmd));}
        for(int i=0;i<blob.Length;i++)
        {
            list.Add(Address(address+(uint)i,cmd));byte[] p=[0x7A,0xB4,0,0,0,0,0,0];BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(2),address+(uint)i+1);
            list.Add(new(p,[0x7A,0xB1,blob[i]],command:cmd,kernel:category=="kernel",state:category=="state",commandByte:category=="command",staging:category=="staging"));
        }
    }
    public TransactionWireStep this[int i]
    {
        get
        {
            if(i<0||i>=Count)throw new ArgumentOutOfRangeException(nameof(i));
            if(i<Binding.Count){var s=Binding[i];byte[] expected=s.Operation==RamSelfTestOperation.Address?s.Payload[..6]:s.Operation==RamSelfTestOperation.VerifyByte?[0x7A,0xB1,original[s.ImageOffset]]:Zyj2RecoveryPolicy.ExpectedIdentity;
                return new(s.Payload,expected,boot:s.Operation==RamSelfTestOperation.VerifyByte);}
            i-=Binding.Count;if(i<install.Count)return install[i];i-=install.Count;
            return i<commands.Count?commands[i]:new(Zyj2RecoveryPolicy.IdentityRequest,Zyj2RecoveryPolicy.ExpectedIdentity);
        }
    }
    public IEnumerator<TransactionWireStep> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
}
public sealed record TransactionTrialDiagnostics(int SentSteps,int BootBytes,int KernelBytes,int StateBytes,int CommandsCompleted,bool AwaitingReply,bool Failed);
/// <summary>Independent native sequencing gate. All caller input is an index into one immutable plan.</summary>
public sealed class TransactionTrialGate(TransactionTrialPlan plan)
{
    private int next,boot,kernel,state,completed,commandBytes,stagingBytes,frames;
    private TransactionWireStep? pending;
    private TransactionReplyCollector? collector;
    private TransactionCommand? current;
    private bool failed;
    public TransactionTrialDiagnostics Diagnostics=>new(next,boot,kernel,state,completed,pending is not null,failed);
    public TransactionWireStep Begin(int index)
    {
        if(failed||pending is not null||index!=next||index<0||index>=plan.Count) {Fail();throw new TransportException("Transaction step blocked by sequence/reply gate.");}
        var step=plan[index];
        if(index>=plan.InstallationEnd && (boot!=28928||kernel!=NativeTransactionPolicy.ImageLength||state!=48)) {Fail();throw new TransportException("Complete boot/code/state verification is required before a transaction.");}
        if(step.Command is not null && !ReferenceEquals(current,step.Command)){current=step.Command;commandBytes=0;stagingBytes=0;}
        if(step.Kind==TransactionReplyKind.Command)
        {
            if(commandBytes!=16||stagingBytes!=(step.Command!.Opcode==TransactionOpcode.StageEcho?128:0)) {Fail();throw new TransportException("Complete command/staging byte verification is required before execution.");}
            collector=new(step.Command!);
        }
        next++;frames=0;pending=step;return step;
    }
    public void Observe(RecoveryFrame frame)
    {
        if(pending is null)return;
        try
        {
            if(++frames>256)throw new TransportException("Transaction reply bound exceeded.");
            if(pending.Kind==TransactionReplyKind.Command)
            {
                collector!.Accept(frame);if(!collector.Complete)return;
                if(!collector.Reply!.ReadStageSuccess)failed=true;completed++;
            }
            else
            {
                if(frame.Status==0x101 && pending.Kind!=TransactionReplyKind.Echo)return;
                if(!pending.Matches(frame))throw new TransportException("Transaction reply/readback mismatch; no retry is allowed.");
                if(pending.BootByte)boot++;if(pending.KernelByte)kernel++;if(pending.StateByte)state++;
                if(pending.CommandByte)commandBytes++;if(pending.StagingByte)stagingBytes++;
            }
            pending=null;
        }
        catch {failed=true;throw;}
    }
    public void Fail()=>failed=true;
}
