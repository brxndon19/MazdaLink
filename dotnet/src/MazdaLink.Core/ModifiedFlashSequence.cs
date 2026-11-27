namespace MazdaLink.Core;

/// <summary>Two fixed blocks on a detached ZYJ2 spare. Original-only factories remain separate.
/// Recovery never resumes a unit: both blocks are re-erased in a new cold session.</summary>
public sealed class ModifiedFlashSequence
{
    private readonly VerifiedRawBackup backup;
    private readonly byte[] intended;
    private RestoreQualificationPlan? first,second;
    public string Id { get; }=Guid.NewGuid().ToString("N");
    public bool Recovery { get; }
    public string OriginalSha256=>RomStorage.Hash(backup.Data);
    public string IntendedSha256=>RomStorage.Hash(intended);
    private ModifiedFlashSequence(VerifiedRawBackup original,byte[] prepared,bool recovery)
    {backup=new(original.Data.ToArray(),original.ReportedCalibration);intended=prepared.ToArray();Recovery=recovery;}
    public static ModifiedFlashSequence Create(VerifiedRawBackup original,byte[] prepared,bool recovery)
    {
        var stable=new VerifiedRawBackup(original.Data.ToArray(),original.ReportedCalibration);
        byte[] desired=prepared.ToArray();Zyj2RecoveryPolicy.RequireBackup(stable);
        RomEditor.ValidateBytes(stable.Data,desired,true);
        if(RomIntegrity.Software(desired)!=Zyj2RecoveryPolicy.SoftwareId)throw new InvalidDataException("Modified bench flashing is limited to ZYJ2EA000ZYV5050 / ZYC3.");
        // Only the independently established EB14 edit and its EB15 correction may differ.
        for(int i=0xE0000;i<TargetPolicy.RomSize;i++)
            if(desired[i]!=stable.Data[i] && (i<0xFF6DC||i>=0xFF6E0))
                throw new InvalidDataException("Modified EB15 changes are limited to the established calibration checksum correction word.");
        return new(stable,desired,recovery);
    }
    public RestoreQualificationPlan First()
    {
        if(first is not null)throw new InvalidOperationException("A modified transaction cannot be reused. Fresh cold binding and whole-block erase are required.");
        first=RestoreQualificationPlan.CreateModified(backup,new(Id,backup.Data,intended,14,Recovery,null));return first;
    }
    public RestoreQualificationPlan Second(RestoreQualificationPlan completedFirst)
    {
        if(first is null||!ReferenceEquals(first,completedFirst)||second is not null||first.Modified is null||!first.Modified.FinalReadsComplete)
            throw new InvalidOperationException("Both exact intermediate reads are required before the second fixed block.");
        second=RestoreQualificationPlan.CreateModified(backup,new(Id,backup.Data,intended,15,Recovery,first.Modified.FinalImage()));return second;
    }
}

/// <summary>Immutable desired data; captures and freezes the live preserved EB15 before any EB14 erase.
/// Only recovery may initially accept unknown bytes in the two writable blocks.</summary>
public sealed class ModifiedFlashStep
{
    private readonly byte[] original,intended,before;
    private readonly bool[] observed=new bool[2048];
    private bool bound;
    private byte[]? after;
    private string? beforeHash,afterHash;
    private int finalPasses;
    public string TransactionId { get; }
    public int Block { get; }
    public bool Recovery { get; }
    public string OriginalSha256 { get; }
    public string IntendedSha256 { get; }
    public string? BeforeSha256=>beforeHash;
    public string? ExpectedAfterSha256=>afterHash;
    public bool FinalReadsComplete=>finalPasses==2;
    internal ModifiedFlashStep(string id,byte[] original,byte[] intended,int block,bool recovery,byte[]? preceding)
    {
        if(block is not (14 or 15)||block==15 && preceding?.Length!=TargetPolicy.RomSize)throw new InvalidDataException("Fixed modified block order is EB14 then EB15.");
        TransactionId=id;this.original=original.ToArray();this.intended=intended.ToArray();Block=block;Recovery=recovery;
        before=(preceding??original).ToArray();OriginalSha256=RomStorage.Hash(this.original);IntendedSha256=RomStorage.Hash(this.intended);
    }
    public byte[] ProgramData(int unit)
    {
        if(unit is <0 or >=1024)throw new ArgumentOutOfRangeException(nameof(unit));
        return intended.AsSpan((Block==14?0xC0000:0xE0000)+unit*128,128).ToArray();
    }
    public byte[] FinalImage()
    {
        if(!bound)throw new InvalidOperationException("The complete pre-erase image has not been bound.");
        return after!.ToArray();
    }
    public void RequireFinal(byte[] bytes)
    {
        if(!bound || !bytes.AsSpan().SequenceEqual(after))throw new InvalidDataException("Complete modified transaction readback mismatch. No request was resent.");
    }
    internal void RequireData(RestoreCommand entry,TransactionReply reply)
    {
        var cmd=entry.Command;
        if(entry.Phase==RestorePhase.UnitReadback)
        {if(!reply.Data.AsSpan().SequenceEqual(ProgramData(cmd.Index)))throw new InvalidDataException("Intended unit readback mismatch. No request was resent.");return;}
        if(cmd.Opcode!=TransactionOpcode.ReadBlock)return;
        if(reply.Data.Length!=512)throw new InvalidDataException("A complete 512-byte read reply is required.");
        int address=cmd.Index*512,start=Block==14?0xC0000:0xE0000;
        if(entry.Phase==RestorePhase.BeforeErase)
        {
            bool unknown=Block==14 && address>=0xC0000;
            if((!unknown || observed[cmd.Index]) && !reply.Data.AsSpan().SequenceEqual(before.AsSpan(address,512)))
                throw new InvalidDataException("Pre-erase full image/preserved-region mismatch. No erase was authorized.");
            if(bound && !observed[cmd.Index])throw new InvalidDataException("The pre-erase snapshot is already frozen.");
            reply.Data.CopyTo(before,address);observed[cmd.Index]=true;
            if(cmd.Index==2047)
            {
                if(observed.Any(x=>!x))throw new InvalidDataException("The complete pre-erase snapshot is required.");
                if(!bound)
                {
                    if(Block==14&&!Recovery)
                    {
                        RomEditor.ValidateBytes(original,before,true);
                        for(int i=0xE0000;i<TargetPolicy.RomSize;i++)
                            if(before[i]!=original[i]&&(i<0xFF6DC||i>=0xFF6E0))throw new InvalidDataException("The current EB15 image is outside the supported tuning policy.");
                    }
                    after=before.ToArray();int selected=Block==14?0xC0000:0xE0000;
                    intended.AsSpan(selected,0x20000).CopyTo(after.AsSpan(selected));
                    beforeHash=RomStorage.Hash(before);afterHash=RomStorage.Hash(after);bound=true;
                }
            }
            return;
        }
        if(!bound)throw new InvalidDataException("A complete live snapshot must precede erase.");
        byte[] expected;
        if(entry.Phase==RestorePhase.EraseReadback)
        {expected=before.AsSpan(address,512).ToArray();if(address>=start&&address<start+0x20000)expected.AsSpan().Fill(255);}
        else expected=after!.AsSpan(address,512).ToArray();
        if(!reply.Data.AsSpan().SequenceEqual(expected))throw new InvalidDataException("Modified blank, preserved or final readback mismatch. No later operation is permitted.");
        // Gate and runner independently call this method. Count by phase, never by duplicate observation.
        if(cmd.Index==2047 && entry.Phase==RestorePhase.FinalReadback1)finalPasses=Math.Max(finalPasses,1);
        if(cmd.Index==2047 && entry.Phase==RestorePhase.FinalReadback2)
        {if(finalPasses<1)throw new InvalidDataException("The first full readback must precede the second.");finalPasses=2;}
    }
}
