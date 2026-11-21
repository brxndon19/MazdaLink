using System.Reflection;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class OriginalBlockRestoreTests
{
    // Synthetic image; production factory still requires the examined private boot fingerprints.
    private static byte[] Image()=>Enumerable.Range(0,TargetPolicy.RomSize).Select(i=>(byte)(i*11+7)).ToArray();
    private static OriginalBlockRestorePlan Plan(byte[] bytes)=>(OriginalBlockRestorePlan)Activator.CreateInstance(
        typeof(OriginalBlockRestorePlan),BindingFlags.Instance|BindingFlags.NonPublic,null,[bytes],null)!;
    [Fact]
    public void PublicRestorePlannerRejectsUnknownOrUnverifiedTargetImages()
    {
        Assert.Throws<InvalidDataException>(()=>OriginalBlockRestorePlan.Create(new(Image(),Zyj2RecoveryPolicy.SoftwareId),Image()));
        Assert.Throws<InvalidDataException>(()=>OriginalBlockRestorePlan.Create(new(Image(),"ZY87EC000ZYV5050"),Image()));
    }
    [Fact]
    public void OriginalRecipeCoversExactlyEb14InImmutableAlignedUnits()
    {
        byte[] bytes=Image();var plan=Plan(bytes);string hash=plan.OriginalSha256;
        Assert.Equal(1024,OriginalBlockRestorePlan.Units);
        for(int i=0;i<1024;i++)
        {
            Assert.Equal(0xC0000+i*128,plan.Address(i));Assert.Equal(0,plan.Address(i)%128);
            Assert.Equal(bytes.AsSpan(plan.Address(i),128).ToArray(),plan.Data(i));
        }
        bytes[0xC0000]^=1;var copy=plan.Data(0);copy[0]^=1;
        Assert.Equal(hash,plan.OriginalSha256);Assert.NotEqual(bytes[0xC0000],plan.Data(0)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan.Data(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>plan.Data(1024));
    }
    [Fact]
    public void ErasedAndCompleteReadbackChecksCannotIgnorePreservedRegions()
    {
        var bytes=Image();var plan=Plan(bytes);plan.RequireFullReadback(bytes);
        foreach(int bad in new[]{0,0x6200,0xC0000,0xDFFFF,0xE0000,0xFFFFF})
        {var copy=bytes.ToArray();copy[bad]^=1;Assert.Throws<InvalidDataException>(()=>plan.RequireFullReadback(copy));}
        var erasedApplication=bytes.ToArray();erasedApplication.AsSpan(0xC0000,0x20000).Fill(255);plan.RequirePreservedReadback(erasedApplication);
        erasedApplication[0xE0000]^=1;Assert.Throws<InvalidDataException>(()=>plan.RequirePreservedReadback(erasedApplication));
        byte[] blank=Enumerable.Repeat((byte)255,0x20000).ToArray();plan.RequireErasedBlock(blank);
        blank[^1]=0;Assert.Throws<InvalidDataException>(()=>plan.RequireErasedBlock(blank));
        Assert.Throws<InvalidDataException>(()=>plan.RequireErasedBlock(new byte[128]));
    }
    [Fact]
    public void EveryAmbiguousUnitBoundaryRequiresFreshBindingAndWholeBlockErase()
    {
        var original=Image();var plan=Plan(original);var target=new FlashModel(original);
        foreach(bool partial in new[]{true,false})
        {
            for(int cut=0;cut<1024;cut++)
            {
                target.Bind(plan);target.Erase(plan);
                for(int unit=0;unit<cut;unit++)target.Program(plan,unit);
                Assert.Throws<PowerLost>(()=>target.Program(plan,cut,cut:true,partial:partial));
                int operations=target.ProgramOperations;
                Assert.Throws<InvalidOperationException>(()=>target.Program(plan,cut));
                Assert.Equal(operations,target.ProgramOperations); // No ambiguous command is resent.
                int erases=target.EraseOperations;
                target.Bind(plan);target.Erase(plan);
                Assert.Equal(erases+1,target.EraseOperations);
                for(int unit=0;unit<1024;unit++)target.Program(plan,unit);
                plan.RequireFullReadback(target.Bytes);
            }
        }
    }
    [Fact]
    public void PartialEraseWithMissingApplicationCanBindAndReconstructOnlyTheSelectedBlock()
    {
        var original=Image();var plan=Plan(original);var target=new FlashModel(original);
        foreach(bool partial in new[]{true,false})
        {
        target.Bind(plan);Assert.Throws<PowerLost>(()=>target.Erase(plan,cut:true,partial:partial));
        Assert.All(target.Bytes.AsSpan(0xC0091,16).ToArray(),b=>Assert.Equal(255,b));
        Assert.Throws<InvalidOperationException>(()=>target.Program(plan,0));
        target.Bind(plan);target.Erase(plan);
        for(int unit=0;unit<1024;unit++)target.Program(plan,unit);
        plan.RequireFullReadback(target.Bytes);
        }
    }
    [Fact]
    public void WrongBootBindingStopsBeforeAnyErase()
    {
        var original=Image();var plan=Plan(original);var target=new FlashModel(original);target.Bytes[0x6200]^=1;
        Assert.Throws<InvalidDataException>(()=>target.Bind(plan));
        Assert.Throws<InvalidOperationException>(()=>target.Erase(plan));Assert.Equal(0,target.EraseOperations);
    }
    [Fact]
    public void UnitReadbackFailureBlocksAllLaterProgrammingUntilAFreshErase()
    {
        var original=Image();var plan=Plan(original);var target=new FlashModel(original);target.Bind(plan);target.Erase(plan);
        Assert.Throws<InvalidDataException>(()=>target.Program(plan,0,corrupt:true));
        Assert.Throws<InvalidOperationException>(()=>target.Program(plan,1));
        Assert.Throws<InvalidOperationException>(()=>target.Program(plan,0));
    }
    [Fact]
    public void OfflinePlannerAndRehearsalOverridesNeverOpenHardware()
    {
        int opens=0;var app=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath(),
            recoveryRehearsal:_=>{opens++;throw new Exception();});
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"original.rom");
        Assert.Equal(1,app.Execute(["original-restore-plan",path]));
        Assert.Equal(1,app.Execute(["original-restore-plan",path,"modified.rom"]));
        Assert.Equal(1,app.Execute(["recovery-rehearsal",path,"external.bin"]));Assert.Equal(0,opens);
    }
    private sealed class PowerLost:Exception;
    // Independent test-only flash model; cannot be plugged into a native adapter.
    private sealed class FlashModel(byte[] initial)
    {
        public byte[] Bytes { get; }=initial.ToArray();
        public int EraseOperations,ProgramOperations;
        private bool bound,erased,tainted;
        private int next;
        private readonly HashSet<int> attempted=[];
        public void Bind(OriginalBlockRestorePlan plan)
        {
            bound=false;erased=false;tainted=true;
            if(RomStorage.Hash(Bytes.AsSpan(0,RecoveryBindingPlan.PreservedBytes))!=plan.PreservedBootSha256)
                throw new InvalidDataException("Preserved boot mismatch");
            plan.RequirePreservedReadback(Bytes);
            bound=true; // Intentionally never consults application calibration ID.
        }
        public void Erase(OriginalBlockRestorePlan plan,bool cut=false,bool partial=true)
        {
            if(!bound)throw new InvalidOperationException();
            bound=false;erased=false;tainted=true;EraseOperations++;
            Bytes.AsSpan(OriginalBlockRestorePlan.Start,OriginalBlockRestorePlan.Length/(partial?2:1)).Fill(255);
            if(cut)throw new PowerLost();
            Bytes.AsSpan(OriginalBlockRestorePlan.Start,OriginalBlockRestorePlan.Length).Fill(255);
            plan.RequireErasedBlock(Bytes.AsSpan(OriginalBlockRestorePlan.Start,OriginalBlockRestorePlan.Length).ToArray());
            next=0;attempted.Clear();tainted=false;erased=true;
        }
        public void Program(OriginalBlockRestorePlan plan,int unit,bool cut=false,bool partial=false,bool corrupt=false)
        {
            if(!erased||tainted||unit!=next||!attempted.Add(unit))throw new InvalidOperationException();
            int address=plan.Address(unit);byte[] data=plan.Data(unit);ProgramOperations++;
            tainted=true; // Intent is recorded before the operation can have any effect.
            for(int i=0;i<(partial?64:128);i++)Bytes[address+i]&=data[i];
            if(cut)throw new PowerLost();
            if(corrupt)Bytes[address]^=1;
            if(!Bytes.AsSpan(address,128).SequenceEqual(data))throw new InvalidDataException("Unit readback mismatch");
            next++;tainted=false;
        }
    }
}
