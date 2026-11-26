using System.Buffers.Binary;
using System.Text.Json;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class Zyj2IntegrityTests
{
    private static byte[] Image()
    {
        byte[] rom=new byte[TargetPolicy.RomSize]; new Random(2026).NextBytes(rom);
        "ZYJ2EA0W"u8.CopyTo(rom.AsSpan(0xC0000)); "ZYJ2EA000ZYV5050"u8.CopyTo(rom.AsSpan(0xC0091));
        for(int i=0;i<19;i++)
        {
            var (start,end)=Zyj2Integrity.Ranges[i]; int p=Zyj2Integrity.TableAddress+i*12;
            BinaryPrimitives.WriteInt32BigEndian(rom.AsSpan(p),start); BinaryPrimitives.WriteInt32BigEndian(rom.AsSpan(p+4),end);
        }
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0xFF650+19*12),uint.MaxValue);
        BinaryPrimitives.WriteInt32BigEndian(rom.AsSpan(0xFFFFC),0x7100);
        return Zyj2Integrity.Correct(rom);
    }
    private sealed class Folder:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"MazdaLink.Tests",Guid.NewGuid().ToString("N"));
        public string Backup=>Path.Combine(Root,"original.rom");
        public Folder(byte[] image)
        {
            Directory.CreateDirectory(Root);string hash=RomStorage.Hash(image);
            foreach(string n in new[]{"original.rom","pass1.read","pass2.read"})File.WriteAllBytes(Path.Combine(Root,n),image);
            File.WriteAllText(Path.Combine(Root,"backup.json"),JsonSerializer.Serialize(new BackupManifest(1,Zyj2RecoveryPolicy.SoftwareId,hash,image.Length,"hardware")));
            File.WriteAllText(Path.Combine(Root,"journal.json"),JsonSerializer.Serialize(new BackupJournal(Zyj2RecoveryPolicy.SoftwareId,hash,hash,true)));
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void IndependentBigEndianSumsVerifyAllNineteenRangesAndEveryCorrectionWord()
    {
        byte[] image=Image();
        foreach(var r in Zyj2Integrity.Check(image))
        {
            ulong total=r.Stored;
            for(int p=r.Start;p<=r.End;p+=4) total+=((uint)image[p]<<24)|((uint)image[p+1]<<16)|((uint)image[p+2]<<8)|image[p+3];
            Assert.Equal(0x5AA5A55Au,(uint)total);Assert.True(r.Valid);
        }
        Assert.Throws<InvalidDataException>(()=>Zy87Integrity.Check(image));
    }
    [Theory][InlineData(0xC0100)][InlineData(0xC0103)][InlineData(0xDFFFF)]
    public void CalibrationEditRequiresEb15CorrectionAndPlanNeverOpensHardware(int offset)
    {
        byte[] original=Image(),edit=original.ToArray();edit[offset]^=1;
        using var folder=new Folder(original);string input=Path.Combine(folder.Root,"edited.rom"),output=Path.Combine(folder.Root,"prepared.rom");File.WriteAllBytes(input,edit);
        Assert.Throws<InvalidDataException>(()=>RomEditor.Validate(folder.Backup,input));
        int opens=0;var text=new StringWriter();var cli=new Terminal(()=>{opens++;throw new Exception("Hardware must stay closed");},new StringReader(""),text,folder.Root);
        Assert.Equal(0,cli.Run(["prepare",folder.Backup,input,output]));
        Assert.Equal(0,cli.Run(["modified-rom-plan",folder.Backup,output]));Assert.Equal(0,opens);
        byte[] prepared=File.ReadAllBytes(output);Assert.All(RomIntegrity.Check(prepared),x=>Assert.True(x.Valid));
        Assert.NotEqual(original.AsSpan(0xFF6DC,4).ToArray(),prepared.AsSpan(0xFF6DC,4).ToArray());
        using var report=JsonDocument.Parse(JsonSerializer.Serialize(ModifiedRomPlan.Create(folder.Backup,output)));
        Assert.False(report.RootElement.GetProperty("eraseOrProgrammingEnabled").GetBoolean());
        Assert.True(report.RootElement.GetProperty("blocks")[1].GetProperty("changedBytes").GetInt32()>0);
        Assert.Equal(edit,File.ReadAllBytes(input));Assert.Equal(original,File.ReadAllBytes(folder.Backup));
        Assert.Throws<IOException>(()=>RomEditor.SaveNew(folder.Backup,input,output));
    }
    [Theory][InlineData(0x10)][InlineData(0x130B2)][InlineData(0xC0008)][InlineData(0xC0091)][InlineData(0xE0100)][InlineData(0xFF650)][InlineData(0xFFFFC)]
    public void ChangedCodeIdentityHeaderRangesOrUnqualifiedCalibrationCannotBePrepared(int offset)
    {
        byte[] original=Image(),edit=original.ToArray();edit[offset]^=1;using var folder=new Folder(original);
        string input=Path.Combine(folder.Root,"bad.rom"),output=Path.Combine(folder.Root,"rejected.rom");File.WriteAllBytes(input,edit);
        Assert.Throws<InvalidDataException>(()=>RomEditor.SaveNew(folder.Backup,input,output));Assert.False(File.Exists(output));
    }
    [Fact]
    public void EveryCorrectionIsRecalculatedAndUneditedExportIsByteExact()
    {
        byte[] original=Image(),edit=original.ToArray();using var folder=new Folder(original);
        for(int i=0;i<19;i++)edit[Zyj2Integrity.CorrectionAddress(i)]^=0x80;
        string input=Path.Combine(folder.Root,"all.rom"),output=Path.Combine(folder.Root,"all-corrected.rom");File.WriteAllBytes(input,edit);
        RomEditor.SaveNew(folder.Backup,input,output);Assert.Equal(original,File.ReadAllBytes(output));
        RomEditor.SaveNew(folder.Backup,folder.Backup,Path.Combine(folder.Root,"unchanged.rom"));Assert.Equal(original,File.ReadAllBytes(Path.Combine(folder.Root,"unchanged.rom")));
    }
    [Theory][InlineData(0xC0100)][InlineData(0xFF650)][InlineData(0xFF734)][InlineData(0xFFFFC)]
    public void InvalidOriginalChecksumOrLayoutCannotAuthorizeAnExport(int offset)
    {
        byte[] original=Image();original[offset]^=1;using var folder=new Folder(original);
        string output=Path.Combine(folder.Root,"bad-original.rom");
        Assert.Throws<InvalidDataException>(()=>RomEditor.SaveNew(folder.Backup,folder.Backup,output));Assert.False(File.Exists(output));
    }
    [Fact]
    public void OfflineReportAndModifiedFlashOverridesCannotAcquireNativeCapabilities()
    {
        int opens=0;var cli=new Terminal(()=>{opens++;throw new Exception();},new StringReader(""),new StringWriter(),Path.GetTempPath());
        Assert.Equal(1,cli.Run(["modified-rom-plan","missing.rom","edited.rom"]));
        Assert.Equal(1,cli.Run(["modified-rom-plan","missing.rom","edited.rom","--write-eb15"]));
        Assert.Equal(1,cli.Run(["flash","modified.rom"]));Assert.Equal(0,opens);
    }
}
