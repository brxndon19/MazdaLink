using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class RestoreQualificationTests
{
    internal static byte[] Image()=>Enumerable.Range(0,TargetPolicy.RomSize).Select(i=>(byte)(i*11+7)).ToArray();
    private static T Make<T>(params object[] args)=>(T)Activator.CreateInstance(typeof(T),BindingFlags.Instance|BindingFlags.NonPublic,null,args,null)!;
    internal static RestoreQualificationPlan Plan(bool recovery=false)
    {
        var b=Image();return Make<RestoreQualificationPlan>(b,Make<RecoveryBindingPlan>(b.AsSpan(0,0x7100).ToArray()),Make<OriginalBlockRestorePlan>(b),recovery,0x12345678u);
    }
    internal static RecoveryFrame[] Reply(RestoreCommand entry,byte[] data)
    {
        var frames=TransactionTests.Reply(entry.Command,data);bool before=entry.Phase is RestorePhase.Initialize or RestorePhase.BeforeErase;
        uint flags=before?1u:99u;uint next=entry.Phase is RestorePhase.PartialReadback or RestorePhase.FinishPartial?1u:entry.Phase is RestorePhase.Program or RestorePhase.UnitReadback?(uint)entry.Command.Index+1:entry.Phase is RestorePhase.FinalReadback1 or RestorePhase.FinalReadback2 or RestorePhase.Finish?1024u:0u;
        var metadata=frames.Where(f=>f.Data[1]==0xF6).ToArray();BinaryPrimitives.WriteUInt32BigEndian(metadata[5].Data.AsSpan(4),flags);BinaryPrimitives.WriteUInt32BigEndian(metadata[6].Data.AsSpan(4),next);return frames;
    }
    [Fact] public void FixedPlanCannotAcceptModifiedDataAddressesOrRepeatedErase()
    {
        var plan=Plan();Assert.Equal(10243,plan.Commands.Count);Assert.Single(plan.Commands,e=>e.Phase==RestorePhase.Erase);
        Assert.Equal(1024,plan.Commands.Count(e=>e.Phase==RestorePhase.Program));Assert.Equal(8192,plan.Commands.Count(e=>e.Command.Opcode==TransactionOpcode.ReadBlock));
        byte[] original=Image();foreach(var e in plan.Commands.Where(e=>e.Phase==RestorePhase.Program))Assert.Equal(original.AsSpan(0xC0000+e.Command.Index*128,128).ToArray(),e.Command.Staging);
        Assert.Equal(OriginalRestoreKernel.ImageSha256,RomStorage.Hash(OriginalRestoreKernel.Image));
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan[-1]);Assert.Throws<ArgumentOutOfRangeException>(()=>plan[plan.Count]);
        Assert.Throws<InvalidDataException>(()=>RestoreQualificationPlan.Create(new(Image(),Zyj2RecoveryPolicy.SoftwareId),"missing",false));
    }
    [Fact] public void OriginalWireSessionDurablyOrdersOneEraseAllUnitsAndTwoFullReads()
    {
        var plan=Plan();using var journal=new MemoryJournal();using var peer=new Peer(plan,journal);
        var result=new RestoreQualificationRunner().Run(peer,plan,_=>{});
        Assert.Equal(1,peer.Erases);Assert.Equal(1024,peer.Programs);Assert.Equal(1024,result.OriginalUnits);Assert.Equal(Image(),peer.Bytes);
        Assert.Equal(2054,journal.Entries.Count);Assert.Equal(("complete",-1),journal.Entries.Last());Assert.Equal(result.FinalRead1Sha256,result.FinalRead2Sha256);
        Assert.Throws<TransportException>(()=>peer.Gate.Begin(0));
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(511)] [InlineData(1023)]
    public void UnknownCompletionStopsAndRecoveryAlwaysRebindsAndReerases(int cut)
    {
        var plan=Plan();using var journal=new MemoryJournal();using var peer=new Peer(plan,journal){Cut=cut};
        Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));int programs=peer.Programs;
        Assert.Throws<TransportException>(()=>peer.SendRestoreStep(peer.Next));Assert.Equal(programs,peer.Programs);
        using var fresh=new MemoryJournal();var recovery=Plan(true);using var recovering=new Peer(recovery,fresh,peer.Bytes);
        new RestoreQualificationRunner().Run(recovering,recovery,_=>{});Assert.Equal(1,recovering.Erases);Assert.Equal(1024,recovering.Programs);Assert.Equal(Image(),recovering.Bytes);
    }
    [Theory] [InlineData("erase-intent")] [InlineData("unit-intent")] [InlineData("unit-verified")]
    public void JournalFailureStopsBeforeAnyLaterDestructiveRequest(string stage)
    {
        var plan=Plan();using var journal=new MemoryJournal{FailStage=stage};using var peer=new Peer(plan,journal);
        Assert.Throws<IOException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));
        Assert.Equal(stage=="erase-intent"?0:1,peer.Erases);Assert.Equal(stage=="unit-verified"?1:0,peer.Programs);
        Assert.Throws<TransportException>(()=>peer.SendRestoreStep(peer.Next));
    }
    [Theory] [InlineData(0)] [InlineData(0xC0000)] [InlineData(0xE0000)]
    public void FirstRestoreRequiresExactCurrentImageBeforeErase(int address)
    {
        var plan=Plan();using var journal=new MemoryJournal();using var peer=new Peer(plan,journal);peer.Bytes[address]^=1;
        if(address==0)Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));else Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));Assert.Equal(0,peer.Erases);
    }
    [Fact] public void RecoveryCannotIgnorePreservedMismatchOrAUnitReadbackFailure()
    {
        var plan=Plan(true);using var journal=new MemoryJournal();using var peer=new Peer(plan,journal);peer.Bytes[0xE0000]^=1;
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));Assert.Equal(0,peer.Erases);
        using var j2=new MemoryJournal();using var bad=new Peer(plan,j2){CorruptUnit=true};
        Assert.Throws<InvalidDataException>(()=>new RestoreQualificationRunner().Run(bad,plan,_=>{}));Assert.Equal(1,bad.Programs);
    }
    [Fact] public void MissingBusyCallbackFullFpfrOrWrongProgressCannotPass()
    {
        var entry=Plan().Commands.First(e=>e.Phase==RestorePhase.Erase);
        foreach(var reply in new[]{new TransactionReply(0x21,2050,0,0,0,3,0,14,0,[]),new(0x21,2050,0,0x100,0,99,0,14,0,[]),new(0x21,2050,0,0,0,99,1,14,0,[])})
            Assert.Throws<TransportException>(()=>RestoreQualificationGate.RequireResult(entry,reply));
    }
    [Fact] public void FailedFullFpfrIsRetainedBeforeAnyProgrammingRequest()
    {
        var plan=Plan();using var journal=new MemoryJournal();using var peer=new Peer(plan,journal){FailEraseFpfr=true};
        Assert.Throws<TransportException>(()=>new RestoreQualificationRunner().Run(peer,plan,_=>{}));
        Assert.Equal(0,peer.Programs);Assert.Equal(0x100u,peer.Gate.LastResult!.FlashResult);Assert.True(peer.Gate.Diagnostics.Failed);
        Assert.Equal(("erase-intent",-1),journal.Entries.Last());
    }
    [Fact] public void GateCannotSkipVerificationOrAdvanceAnUnansweredStep()
    {
        var plan=Plan();using var journal=new MemoryJournal();var gate=new RestoreQualificationGate(plan,journal);gate.Begin(0);
        Assert.Throws<TransportException>(()=>gate.Begin(1));Assert.True(gate.Diagnostics.Failed);
        using var j2=new MemoryJournal();gate=new(plan,j2);Assert.Throws<TransportException>(()=>gate.Begin(plan.InstallationEnd));
    }
    [Fact] public void RestoreCliRefusesMissingEvidenceAndOverridesBeforeHardwareOpen()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),originalRestore:(_,_)=>{opens++;throw new Exception();});
        foreach(string cmd in new[]{"original-restore-check","original-restore-recover"})
        {
            Assert.Equal(1,app.Execute([cmd,"missing/original.rom","missing/events.jsonl"]));
            Assert.Equal(1,app.Execute([cmd,"original.rom","events.jsonl","modified.rom"]));
        }
        Assert.Equal(0,opens);
    }
    [Fact] public void MalformedOrIncompletePrerequisiteCannotQualifyTheOriginal()
    {
        string path=Path.GetTempFileName();try{
            File.WriteAllText(path,"{}");Assert.Throws<InvalidDataException>(()=>RestorePrerequisite.Require(path,new(Image(),Zyj2RecoveryPolicy.SoftwareId)));
            File.WriteAllLines(path,Enumerable.Repeat("{}",102));Assert.Throws<InvalidDataException>(()=>RestorePrerequisite.Require(path,new(Image(),Zyj2RecoveryPolicy.SoftwareId)));
        }finally{File.Delete(path);}
    }
    [Fact] public void FullQualificationJournalIsFlushedHashChainedAndContainsNoRomPayload()
    {
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var plan=Plan();string path=Path.Combine(folder,"transaction.jsonl");
            using(var real=new RestoreJournal(path,plan.Original))using(var journal=new MemoryJournal{Mirror=real})using(var peer=new Peer(plan,journal))
                new RestoreQualificationRunner().Run(peer,plan,_=>{});
            string[] lines=File.ReadAllLines(path);Assert.Equal(2054,lines.Length);string previous=new('0',64);
            for(int i=0;i<lines.Length;i++)
            {
                var entry=JsonSerializer.Deserialize<RestoreJournalEntry>(lines[i])!;Assert.Equal(i+1,entry.Sequence);Assert.Equal(previous,entry.PreviousHash);
                Assert.Equal(plan.Original.OriginalSha256,entry.OriginalSha256);previous=RomStorage.Hash(System.Text.Encoding.UTF8.GetBytes(lines[i]));
            }
            Assert.Equal("complete",JsonSerializer.Deserialize<RestoreJournalEntry>(lines[^1])!.Stage);
        }
        finally{Directory.Delete(folder,true);}
    }
    internal sealed class MemoryJournal:IRestoreJournal
    {
        internal readonly List<(string Stage,int Unit)> Entries=[];public string? FailStage;public IRestoreJournal? Mirror;
        public void Commit(string stage,int unit=-1){if(stage==FailStage)throw new IOException();Mirror?.Commit(stage,unit);Entries.Add((stage,unit));}public void Dispose(){}
    }
    internal sealed class Peer:IOriginalRestoreChannel
    {
        private readonly RestoreQualificationPlan plan;private readonly MemoryJournal journal;private readonly Queue<RecoveryFrame> frames=[];private readonly byte[] kernel=OriginalRestoreKernel.Image;
        public readonly RestoreQualificationGate Gate;public readonly byte[] Bytes;public int Next,Erases,Programs,Cut=-2;public bool CorruptUnit,FailEraseFpfr;
        public Peer(RestoreQualificationPlan plan,MemoryJournal journal,byte[]? initial=null){this.plan=plan;this.journal=journal;Gate=new(plan,journal);Bytes=initial?.ToArray()??Image();}
        public void SendRestoreStep(int index,CancellationToken cancel=default)
        {
            cancel.ThrowIfCancellationRequested();Assert.Empty(frames);Assert.Equal(Next++,index);var step=Gate.Begin(index);var cmd=step.Command;
            if(step.Kind==TransactionReplyKind.Command)
            {
                var entry=plan.CommandFor(index)!;byte[] data=[];
                if(cmd!.Opcode==TransactionOpcode.Erase){Assert.Equal(("erase-intent",-1),journal.Entries.Last());Erases++;Bytes.AsSpan(0xC0000,0x20000).Fill(255);}
                if(cmd.Opcode==TransactionOpcode.Program){Assert.Equal(("unit-intent",(int)cmd.Index),journal.Entries.Last());Programs++;cmd.Staging!.CopyTo(Bytes,0xC0000+cmd.Index*128);}
                if((cmd.Opcode==TransactionOpcode.Erase&&Cut==-1)||(cmd.Opcode==TransactionOpcode.Program&&Cut==cmd.Index)){Gate.Fail();throw new TransportException("Simulated missing completion after effect.");}
                if(cmd.Opcode==TransactionOpcode.ReadBlock)data=Bytes.AsSpan(cmd.Index*512,512).ToArray();
                if(cmd.Opcode==TransactionOpcode.VerifyOriginalUnit){data=Bytes.AsSpan(0xC0000+cmd.Index*128,128).ToArray();if(CorruptUnit)data[0]^=1;}
                var replies=Reply(entry,data);
                if(FailEraseFpfr && cmd.Opcode==TransactionOpcode.Erase)BinaryPrimitives.WriteUInt32BigEndian(replies.Where(f=>f.Data[1]==0xF6).ElementAt(3).Data.AsSpan(4),0x100);
                foreach(var f in replies)frames.Enqueue(f);return;
            }
            if(step.Kind==TransactionReplyKind.Echo){frames.Enqueue(new(0,0x101,[]));return;}
            byte[] p=step.Payload,r=new byte[8];
            if(p[1]==0x9c)p.AsSpan(0,6).CopyTo(r);
            else if(p[1]==0xb4){uint a=BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2))-1;r[0]=0x7a;r[1]=0xb1;r[2]=step.BootByte?Bytes[(int)a]:step.KernelByte?kernel[(int)(a-0xffff8000)]:step.StateByte?(byte)0:step.CommandByte?cmd!.Descriptor()[(int)(a-0xffffa080)]:cmd!.Staging![(int)(a-0xffffa000)];}
            else r=Zyj2RecoveryPolicy.ExpectedIdentity;frames.Enqueue(new(0x21,0x100,r));
        }
        public RecoveryFrame? Read(int timeout){var f=frames.Dequeue();Gate.Observe(f);return f;}
        internal int PendingFrames=>frames.Count;
        public string? CleanupWarning=>null;public void Open(){}public void StartEntryPulse(){}public void StopEntryPulse(){}public void ClearReceive(){}public void RequestIdentity(){}public void Dispose(){}
    }
}
