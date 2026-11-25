using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class TransactionTests
{
    private static byte[] Image()=>Enumerable.Range(0,TargetPolicy.RomSize).Select(i=>(byte)(i*11+7)).ToArray();
    private static T Make<T>(params object[] args)=>(T)Activator.CreateInstance(typeof(T),BindingFlags.Instance|BindingFlags.NonPublic,null,args,null)!;
    internal static TransactionTrialPlan Plan()
    {
        byte[] bytes=Image();var binding=Make<RecoveryBindingPlan>(bytes.AsSpan(0,0x7100).ToArray());
        return Make<TransactionTrialPlan>(bytes,binding,0x12345678u);
    }
    private static TransactionCommand Command(TransactionOpcode opcode=TransactionOpcode.ReadBlock,byte[]? data=null)=>
        Make<TransactionCommand>(opcode,(ushort)2,0x12345678u,(ushort)0,data!);
    internal static RecoveryFrame[] Reply(TransactionCommand cmd,byte[] data,byte status=0,uint fpfr=0)
    {
        byte[] meta=new byte[32];void U(int p,uint v)=>BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(p),v);
        U(0,0x4D4C5431);U(4,cmd.Cookie);BinaryPrimitives.WriteUInt16BigEndian(meta.AsSpan(8),cmd.Sequence);
        meta[10]=(byte)cmd.Opcode;meta[11]=status;U(12,fpfr);U(16,data.Length==0?0:TransactionCrc32.Calculate(data));U(20,1);U(28,(uint)cmd.Index<<16);
        var frames=new List<RecoveryFrame>();
        void Stream(byte kind,byte[] bytes)
        {for(int i=0;i<bytes.Length;i+=4){byte[] p=[0x7A,kind,0,0,0,0,0,0];BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2),(ushort)(i/4));bytes.AsSpan(i,4).CopyTo(p.AsSpan(4));frames.Add(new(0x21,0x100,p));}}
        Stream(0xD6,data);Stream(0xF6,meta);
        frames.Add(new(0x21,0x100,[0x7A,0xC6,0x4D,0x54,(byte)cmd.Opcode,status,(byte)(cmd.Sequence>>8),(byte)cmd.Sequence]));return frames.ToArray();
    }
    [Fact] public void CrcMatchesAnIndependentPublishedVector()=>Assert.Equal(0xCBF43926u,TransactionCrc32.Calculate("123456789"u8));
    [Fact] public void TrialContainsOnlyFixedReadStageCommandsAndImmutableCode()
    {
        var plan=Plan();Assert.Equal(2058,plan.Commands.Count);Assert.Equal(2048,plan.Commands.Count(c=>c.Opcode==TransactionOpcode.ReadBlock));
        Assert.DoesNotContain(plan.Commands,c=>c.Opcode is TransactionOpcode.Erase or TransactionOpcode.Program);
        Assert.False(NativeTransactionPolicy.HardwareFlashEnabled);
        Assert.Equal(NativeTransactionPolicy.ImageSha256,RomStorage.Hash(NativeTransactionPolicy.Image));
        byte[] image=NativeTransactionPolicy.Image;image[0]^=1;Assert.NotEqual(image,NativeTransactionPolicy.Image);
        byte[] data=plan.Commands.First(c=>c.Opcode==TransactionOpcode.StageEcho).Staging!;data[0]^=1;
        Assert.NotEqual(data,plan.Commands.First(c=>c.Opcode==TransactionOpcode.StageEcho).Staging);
        Assert.Throws<InvalidDataException>(()=>TransactionTrialPlan.Create(new(Image(),Zyj2RecoveryPolicy.SoftwareId)));
    }
    [Fact] public void OrderedFullReplyMustMatchCrcCookieSequenceAndIndex()
    {
        var cmd=Command();var collector=new TransactionReplyCollector(cmd);
        foreach(var frame in Reply(cmd,Image()[..512]))collector.Accept(frame);
        Assert.True(collector.Complete);Assert.True(collector.Reply!.ReadStageSuccess);Assert.Equal(Image()[..512],collector.Reply.Data);
        Assert.Throws<TransportException>(()=>collector.Accept(new(0x21,0x100,new byte[8])));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void CorruptMissingDuplicatedOrReorderedFramesNeverPass(int fault)
    {
        var cmd=Command();var frames=Reply(cmd,Image()[..512]).ToList();
        switch(fault)
        {
            case 0:frames[0].Data[4]^=1;break;
            case 1:frames.RemoveAt(32);break;
            case 2:frames.Insert(32,frames[31]);break;
            case 3:frames[129].Data[4]^=1;break; // Cookie
            case 4:frames[130].Data[5]^=1;break; // Sequence
            case 5:frames[135].Data[4]^=1;break; // Requested index
        }
        var collector=new TransactionReplyCollector(cmd);
        Assert.Throws<TransportException>(()=>{foreach(var frame in frames)collector.Accept(frame);});Assert.False(collector.Complete);
    }
    [Fact] public void FullFpfrAndFailedStagingRemainFailedEvenWithZeroLowByte()
    {
        var cmd=Command();var c=new TransactionReplyCollector(cmd);foreach(var f in Reply(cmd,[],17,0x100))c.Accept(f);
        Assert.True(c.Complete);Assert.False(c.Reply!.ReadStageSuccess);Assert.Equal(0x100u,c.Reply.FlashResult);
        byte[] data=Image()[..128];var stage=Command(TransactionOpcode.StageEcho,data);data[0]^=1;
        var collector=new TransactionReplyCollector(stage);
        Assert.Throws<TransportException>(()=>{foreach(var f in Reply(stage,data))collector.Accept(f);});
    }
    [Fact] public void TrialRunnerAndIndependentGateVerifyTheWholeImageBeforeStaging()
    {
        var plan=Plan();using var peer=new Peer(plan);byte[]? capture=null;
        var result=new TransactionTrialRunner().Run(peer,plan,_=>{},observeReadback:bytes=>capture=bytes);
        Assert.Equal(Image(),capture);Assert.Equal(1048576,result.ReadBytes);Assert.Equal(8,result.StagedUnits);Assert.Equal(2058,result.Commands);
        Assert.Equal(plan.Count,peer.Next);Assert.False(peer.Gate.Diagnostics.Failed);
    }
    [Fact] public void ChangedCurrentRomStopsBeforeRamStagingWithoutOverwritingBackup()
    {
        var plan=Plan();using var peer=new Peer(plan){Changed=true};
        Assert.Throws<InvalidDataException>(()=>new TransactionTrialRunner().Run(peer,plan,_=>{}));
        Assert.Equal(0,peer.Stages);
    }
    [Fact] public void GateCannotSkipReplyReadbackOrRestartAfterFailure()
    {
        var gate=new TransactionTrialGate(Plan());gate.Begin(0);
        Assert.Throws<TransportException>(()=>gate.Begin(1));Assert.True(gate.Diagnostics.Failed);
        Assert.Throws<TransportException>(()=>gate.Begin(0));
        gate=new(Plan());Assert.Throws<TransportException>(()=>gate.Begin(57857));
    }
    [Fact] public void DurableIntentPrecedesEveryOperationAndTwoFullReadbacksComplete()
    {
        byte[] image=Image();var plan=Make<OriginalBlockRestorePlan>(image);using var journal=new MemoryJournal();var device=new Device(image,journal);
        var session=new OriginalRestoreTransaction(plan,device,journal);session.Run();
        Assert.Equal(1,device.Erases);Assert.Equal(1024,device.Programs);Assert.Equal("complete",journal.Entries.Last().Stage);
        Assert.Equal(2054,journal.Entries.Count);Assert.Throws<InvalidOperationException>(()=>session.Run());
        Assert.Equal(image,device.Bytes);
    }
    [Theory] [InlineData("erase-intent")] [InlineData("unit-intent")] [InlineData("unit-verified")]
    public void JournalFailureCannotAdvanceToTheNextDestructiveRequest(string stage)
    {
        var image=Image();var plan=Make<OriginalBlockRestorePlan>(image);using var journal=new MemoryJournal{FailStage=stage};var device=new Device(image,journal);
        var session=new OriginalRestoreTransaction(plan,device,journal);
        Assert.Throws<IOException>(()=>session.Run());Assert.Throws<InvalidOperationException>(()=>session.Run());
        Assert.Equal(stage=="erase-intent"?0:1,device.Erases);Assert.Equal(stage=="unit-verified"?1:0,device.Programs);
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(511)] [InlineData(1023)]
    public void AmbiguousCompletionCannotResendAndNewSessionAlwaysReerases(int cut)
    {
        var image=Image();var plan=Make<OriginalBlockRestorePlan>(image);using var journal=new MemoryJournal();var device=new Device(image,journal){Cut=cut};
        var session=new OriginalRestoreTransaction(plan,device,journal);
        Assert.Throws<TimeoutException>(()=>session.Run());int operations=device.Programs;
        Assert.Throws<InvalidOperationException>(()=>session.Run());Assert.Equal(operations,device.Programs);
        using var fresh=new MemoryJournal();device.Journal=fresh;device.Cut=-2;new OriginalRestoreTransaction(plan,device,fresh).Run();
        Assert.Equal(2,device.Erases);Assert.Equal(image,device.Bytes);
    }
    [Fact] public void JournalIsCreateNewBoundedFlushedAndHashChainedWithoutRomPayload()
    {
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var plan=Make<OriginalBlockRestorePlan>(Image());string path=Path.Combine(folder,"events.jsonl");
            using(var journal=new RestoreJournal(path,plan)){journal.Commit("started");journal.Commit("bound");journal.Commit("erase-intent");}
            Assert.Throws<IOException>(()=>new RestoreJournal(path,plan));string[] lines=File.ReadAllLines(path);
            var entries=lines.Select(l=>JsonSerializer.Deserialize<RestoreJournalEntry>(l)!).ToArray();
            Assert.Equal(new string('0',64),entries[0].PreviousHash);
            Assert.Equal(RomStorage.Hash(System.Text.Encoding.UTF8.GetBytes(lines[0])),entries[1].PreviousHash);
            Assert.All(entries,e=>Assert.Equal(plan.OriginalSha256,e.OriginalSha256));Assert.Equal(3,entries[^1].Sequence);
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact] public void MissingBackupsExternalCodeAndRangesCannotOpenNativeTrialHardware()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            transactionTrial:_=>{opens++;throw new Exception();});
        Assert.Equal(1,app.Execute(["native-transaction-check","missing/original.rom"]));
        Assert.Equal(1,app.Execute(["native-transaction-check","missing/original.rom","external.bin"]));
        Assert.Equal(1,app.Execute(["native-transaction-check","missing/original.rom","0xc0000"]));Assert.Equal(0,opens);
    }
    [Fact] public void PreservedMismatchAndCancellationCannotIssueErase()
    {
        var image=Image();var plan=Make<OriginalBlockRestorePlan>(image);using var journal=new MemoryJournal();var device=new Device(image,journal);
        device.Bytes[0xe0000]^=1;
        Assert.Throws<InvalidDataException>(()=>new OriginalRestoreTransaction(plan,device,journal).Run());Assert.Equal(0,device.Erases);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>new OriginalRestoreTransaction(plan,device,journal).Run(cancel.Token));Assert.Equal(0,device.Erases);
    }
    private sealed class MemoryJournal:IRestoreJournal
    {
        public List<(string Stage,int Unit)> Entries=[];public string? FailStage;
        public void Commit(string stage,int unit=-1){if(stage==FailStage)throw new IOException();Entries.Add((stage,unit));}
        public void Dispose(){}
    }
    private sealed class Device(byte[] image,MemoryJournal journal):IOriginalRestoreDevice
    {
        public byte[] Bytes=image.ToArray();public MemoryJournal Journal=journal;public int Erases,Programs,Cut=-2;
        public void ColdBind(string hash){Assert.Equal(hash,RomStorage.Hash(Bytes.AsSpan(0,0x7100)));}
        public byte[] ReadImage()=>Bytes.ToArray();
        public void EraseEb14(){Assert.Equal(("erase-intent",-1),Journal.Entries.Last());Erases++;Bytes.AsSpan(0xC0000,0x20000).Fill(255);if(Cut==-1)throw new TimeoutException();}
        public void ProgramNextOriginalUnit(int index,byte[] data){Assert.Equal(("unit-intent",index),Journal.Entries.Last());Programs++;data.CopyTo(Bytes,0xC0000+128*index);if(Cut==index)throw new TimeoutException();}
        public byte[] ReadUnit(int index)=>Bytes.AsSpan(0xC0000+128*index,128).ToArray();
    }
    private sealed class Peer(TransactionTrialPlan plan):ITransactionTrialChannel
    {
        private readonly byte[] original=Image(),kernel=NativeTransactionPolicy.Image;
        public readonly TransactionTrialGate Gate=new(plan);public int Next,Stages;public bool Changed;
        private readonly Queue<RecoveryFrame> frames=[];
        public void SendTrialStep(int index)
        {
            Assert.Empty(frames);Assert.Equal(Next++,index);var step=Gate.Begin(index);var cmd=step.Command;
            if(step.Kind==TransactionReplyKind.Command)
            {
                byte[] data=cmd!.Opcode==TransactionOpcode.ReadBlock?original.AsSpan(cmd.Index*512,512).ToArray():cmd.Staging??[];
                if(Changed && cmd.Opcode==TransactionOpcode.ReadBlock && cmd.Index==0)data[0]^=1;
                if(cmd.Opcode==TransactionOpcode.StageEcho)Stages++;
                foreach(var f in Reply(cmd,data))frames.Enqueue(f);
            }
            else if(step.Kind==TransactionReplyKind.Echo)frames.Enqueue(new(0,0x101,[]));
            else
            {
                byte[] p=step.Payload;byte[] data=new byte[8];
                if(p[1]==0x9C)p.AsSpan(0,6).CopyTo(data);
                else if(p[1]==0xB4)
                {
                    uint address=BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2))-1;data[0]=0x7A;data[1]=0xB1;
                    data[2]=step.BootByte?original[(int)address]:step.KernelByte?kernel[(int)(address-0xFFFF8000)]:step.StateByte?(byte)0:step.CommandByte?cmd!.Descriptor()[(int)(address-0xFFFFA080)]:cmd!.Staging![(int)(address-0xFFFFA000)];
                }
                else data=Zyj2RecoveryPolicy.ExpectedIdentity;
                frames.Enqueue(new(0x21,0x100,data));
            }
        }
        public RecoveryFrame? Read(int timeout){var frame=frames.Dequeue();Gate.Observe(frame);return frame;}
        public string? CleanupWarning=>null;
        public void Open(){}public void StartEntryPulse(){}public void StopEntryPulse(){}public void ClearReceive(){}public void RequestIdentity(){}public void Dispose(){}
    }
}
