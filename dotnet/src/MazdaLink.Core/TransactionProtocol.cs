using System.Buffers.Binary;

namespace MazdaLink.Core;

public enum TransactionOpcode:byte { Initialize=0, ReadBlock=0x11, StageEcho=0x12, VerifyOriginalUnit=0x13, Erase=0x21, Program=0x22, Finish=0x7F }
public static class TransactionCrc32
{
    public static uint Calculate(ReadOnlySpan<byte> data)
    {
        uint value=uint.MaxValue;
        foreach(byte b in data) { value^=b;for(int i=0;i<8;i++)value=(value>>1)^(0xEDB88320u & (0u-(value&1))); }
        return ~value;
    }
}
public sealed class TransactionCommand
{
    private readonly byte[]? staging;
    public TransactionOpcode Opcode { get; }
    public ushort Sequence { get; }
    public uint Cookie { get; }
    public ushort Index { get; }
    public byte[]? Staging => staging?.ToArray();
    public int DataLength => Opcode==TransactionOpcode.ReadBlock?512:Opcode is TransactionOpcode.StageEcho or TransactionOpcode.VerifyOriginalUnit?128:0;
    internal TransactionCommand(TransactionOpcode opcode,ushort sequence,uint cookie,ushort index=0,byte[]? staging=null)
    {
        if(sequence==0||cookie==0)throw new ArgumentException("A nonzero command identity is required.");
        if(opcode==TransactionOpcode.ReadBlock && index>=2048 || opcode is TransactionOpcode.StageEcho or TransactionOpcode.Program && (index>=1024||staging?.Length!=128) || opcode==TransactionOpcode.VerifyOriginalUnit && (index>=1024||staging is not null) || opcode==TransactionOpcode.Erase && (index!=14||staging is not null))throw new ArgumentOutOfRangeException(nameof(index));
        Opcode=opcode;Sequence=sequence;Cookie=cookie;Index=index;this.staging=staging?.ToArray();
    }
    public byte[] Descriptor()
    {
        byte[] p=new byte[16];p[0]=(byte)Opcode;BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2),Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(4),Cookie);BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(8),Index);
        if(staging is not null)BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(12),TransactionCrc32.Calculate(staging));return p;
    }
}
public sealed record TransactionReply(byte Opcode,ushort Sequence,byte Status,uint FlashResult,uint DataCrc32,uint Flags,uint NextUnit,ushort Index,uint Fccs,byte[] Data)
{
    public bool ReadStageSuccess=>Status==0 && FlashResult==0 && Flags==1 && NextUnit==0 && Fccs==0;
}
/// <summary>Strict, bounded, sequence/CRC-bound multi-frame collector. No resend.</summary>
public sealed class TransactionReplyCollector(TransactionCommand command)
{
    private readonly byte[] data=new byte[command.DataLength],metadata=new byte[32];
    private int dataFrames,metaFrames,frames;
    private bool metaStarted;
    public bool Complete { get; private set; }
    public TransactionReply? Reply { get; private set; }
    public void Accept(RecoveryFrame frame)
    {
        if(Complete)throw new TransportException("Unexpected transaction frame after completion.");
        if(++frames>256)throw new TransportException("Transaction frame bound exceeded.");
        if(frame.Status==0x101)return; // Adapter echo is not ECU evidence.
        if(frame.Id!=Zyj2RecoveryPolicy.ResponseId||frame.Status!=0x100||frame.Data.Length!=8)throw new TransportException("Invalid transaction frame metadata.");
        var p=frame.Data;
        if(p[0]==0x7A && p[1]==0xD6)
        {
            if(metaStarted||dataFrames>=data.Length/4||BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2))!=dataFrames)throw new TransportException("Transaction data frame is missing, duplicated or out of order.");
            p.AsSpan(4,4).CopyTo(data.AsSpan(dataFrames++*4));return;
        }
        if(p[0]==0x7A && p[1]==0xF6)
        {
            metaStarted=true;
            if(metaFrames>=8||BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2))!=metaFrames)throw new TransportException("Transaction metadata frame sequence failed.");
            p.AsSpan(4,4).CopyTo(metadata.AsSpan(metaFrames++*4));return;
        }
        if(!p.AsSpan(0,4).SequenceEqual(new byte[]{0x7A,0xC6,0x4D,0x54})||metaFrames!=8)throw new TransportException("Transaction result marker or metadata is incomplete.");
        uint U(int offset)=>BinaryPrimitives.ReadUInt32BigEndian(metadata.AsSpan(offset));
        ushort sequence=BinaryPrimitives.ReadUInt16BigEndian(metadata.AsSpan(8));
        if(U(0)!=0x4D4C5431||U(4)!=command.Cookie||sequence!=command.Sequence||metadata[10]!=(byte)command.Opcode||(ushort)(U(28)>>16)!=command.Index||(U(28)&0xFF00)!=0||p[4]!=metadata[10]||p[5]!=metadata[11]||BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(6))!=sequence)
            throw new TransportException("Transaction command identity did not match its result.");
        if(metadata[11]==0 && (dataFrames*4!=data.Length || U(16)!=(data.Length==0?0:TransactionCrc32.Calculate(data))))
            throw new TransportException("Transaction data length or CRC32 did not match.");
        if(metadata[11]==0 && command.Opcode==TransactionOpcode.StageEcho && !data.AsSpan().SequenceEqual(command.Staging))throw new TransportException("RAM staging echo differs from the selected original unit.");
        Reply=new(metadata[10],sequence,metadata[11],U(12),U(16),U(20),U(24),(ushort)(U(28)>>16),U(28)&255,data.ToArray());Complete=true;
    }
}
