namespace MazdaLink.Core;

/// <summary>Offline recipe only. No transport or erase/program capability exists here.</summary>
public sealed class OriginalBlockRestorePlan
{
    public const int BlockNumber=14, Start=0xC0000, Length=0x20000, ProgramUnit=128, Units=Length/ProgramUnit;
    private readonly byte[] original;
    public string OriginalSha256 { get; }
    public string BlockSha256 { get; }
    public string PreservedBootSha256 { get; }
    public int SelectedBlock { get; }
    public int SelectedStart=>SelectedBlock==15?0xE0000:Start;
    private OriginalBlockRestorePlan(byte[] bytes):this(bytes,14) { }
    private OriginalBlockRestorePlan(byte[] bytes,int block)
    {
        if(bytes.Length!=TargetPolicy.RomSize) throw new InvalidDataException("A complete original image is required.");
        if(block is not (14 or 15))throw new InvalidDataException("Unsupported fixed restoration block.");
        SelectedBlock=block;original=bytes.ToArray();OriginalSha256=RomStorage.Hash(original);
        BlockSha256=RomStorage.Hash(original.AsSpan(SelectedStart,Length));
        PreservedBootSha256=RomStorage.Hash(original.AsSpan(0,RecoveryBindingPlan.PreservedBytes));
    }
    public static OriginalBlockRestorePlan Create(VerifiedRawBackup backup,byte[] intended)
    {
        var snapshot=new VerifiedRawBackup(backup.Data.ToArray(),backup.ReportedCalibration);
        Zyj2RecoveryPolicy.RequireBackup(snapshot);
        if(!snapshot.Data.AsSpan().SequenceEqual(intended.ToArray()))
            throw new InvalidDataException("Only the exact unchanged original backup can be planned. Modified images are not accepted.");
        return new(snapshot.Data);
    }
    internal static OriginalBlockRestorePlan CreateEb15(VerifiedRawBackup backup)
    {
        var stable=new VerifiedRawBackup(backup.Data.ToArray(),backup.ReportedCalibration);
        Zyj2RecoveryPolicy.RequireBackup(stable);Zyj2Integrity.RequireLayout(stable.Data);
        if(Zyj2Integrity.Check(stable.Data).Any(x=>!x.Valid))throw new InvalidDataException("Original EB15 qualification requires all original checksums to pass.");
        return new(stable.Data,15);
    }
    public int Address(int unit)=>unit is <0 or >=Units?throw new ArgumentOutOfRangeException(nameof(unit)):SelectedStart+unit*ProgramUnit;
    public byte[] Data(int unit)=>original.AsSpan(Address(unit),ProgramUnit).ToArray();
    public void RequireErasedBlock(byte[] readback)
    {
        if(readback.Length!=Length || readback.Any(b=>b!=255))
            throw new InvalidDataException("The complete selected block must read as erased before any programming.");
    }
    public void RequirePreservedReadback(byte[] readback)
    {
        if(readback.Length!=original.Length || !original.AsSpan(0,SelectedStart).SequenceEqual(readback.AsSpan(0,SelectedStart)) ||
            !original.AsSpan(SelectedStart+Length).SequenceEqual(readback.AsSpan(SelectedStart+Length)))
            throw new InvalidDataException($"A preserved region outside EB{SelectedBlock} differs from the original backup.");
    }
    public void RequireFullReadback(byte[] readback)
    {
        if(!original.AsSpan().SequenceEqual(readback))
            throw new InvalidDataException("Complete readback does not exactly match the intended original image, including preserved blocks.");
    }
    public object Report()=>new { schema=1,hardwareAccessed=false,eraseOrProgrammingEnabled=false,
        targetProfile=Zyj2RecoveryPolicy.SoftwareId,expectedHardwareClass=1,blockNumber=SelectedBlock,
        startAddress=SelectedStart,blockLength=Length,programUnit=ProgramUnit,programUnits=Units,
        originalSha256=OriginalSha256,blockSha256=BlockSha256,preservedBootSha256=PreservedBootSha256,
        imagePolicy="exact selected original backup; no edits; not an OEM-stock authentication",
        recoveryPolicy=$"fresh cold boot binding; re-erase complete EB{SelectedBlock} after any ambiguous operation; never resend a program unit",
        verificationPolicy="exact preserved-region check before erase, complete erased-block check, exact unit readback, then matching full-image reads",
        qualification="offline plan only; actual erased-application entry and interrupted-write recovery unqualified" };
}
