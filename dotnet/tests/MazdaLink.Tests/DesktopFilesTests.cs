using System.Text.Json;
using MazdaLink.Core;
namespace MazdaLink.Tests;

public sealed class DesktopFilesTests
{
    private sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"MazdaLink.UI.Tests",Guid.NewGuid().ToString("N"));
        public string Original=>Path.Combine(Root,"original.rom");
        public byte[] Bytes {get;}=ModifiedFlashTests.Original();
        public Fixture()
        {
            Directory.CreateDirectory(Root);string hash=RomStorage.Hash(Bytes);
            foreach(string n in new[]{"original.rom","pass1.read","pass2.read"})File.WriteAllBytes(Path.Combine(Root,n),Bytes);
            File.WriteAllText(Path.Combine(Root,"backup.json"),JsonSerializer.Serialize(new BackupManifest(1,Zyj2RecoveryPolicy.SoftwareId,hash,Bytes.Length,"hardware")));
            File.WriteAllText(Path.Combine(Root,"journal.json"),JsonSerializer.Serialize(new BackupJournal(Zyj2RecoveryPolicy.SoftwareId,hash,hash,true)));
        }
        public string Edit(bool permitted=true)
        {var b=Bytes.ToArray();b[permitted?0xD54F0:0xC0008]^=1;string path=Path.Combine(Root,Guid.NewGuid().ToString("N")+".bin");File.WriteAllBytes(path,b);return path;}
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]public void LoadingEditedBinAutomaticallyPreparesEveryChecksumAndKeepsInputsIntact()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);string edit=f.Edit();byte[] before=File.ReadAllBytes(edit);
        string prepared=files.Prepare(edit);Assert.EndsWith(".bin",prepared);Assert.NotEqual(edit,prepared);Assert.Equal(prepared,files.RequirePrepared());
        Assert.Equal(1,files.CalibrationChanges);Assert.All(Zyj2Integrity.Check(File.ReadAllBytes(prepared)),r=>Assert.True(r.Valid));
        Assert.Equal(before,File.ReadAllBytes(edit));Assert.Equal(f.Bytes,File.ReadAllBytes(f.Original));Assert.Equal(RomStorage.Hash(File.ReadAllBytes(prepared)),files.PreparedSha256);
    }
    [Fact]public void ChangedPreparedFileCannotSilentlyReplaceTheReviewedTargetEvenWithValidChecksums()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);string path=files.Prepare(f.Edit());
        byte[] changed=File.ReadAllBytes(path);changed[0xC0123]^=1;File.WriteAllBytes(path,Zyj2Integrity.Correct(changed));
        Assert.Throws<InvalidDataException>(()=>files.RequirePrepared());
    }
    [Fact]public void ChangedOriginalCaptureCannotRemainAnAuthorizedDesktopBackup()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);files.Prepare(f.Edit());
        File.WriteAllBytes(f.Original,new byte[TargetPolicy.RomSize]);Assert.Throws<InvalidDataException>(()=>files.RequirePrepared());
    }
    [Fact]public void AFailedFileLoadClearsThePreviousWriteTarget()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);files.Prepare(f.Edit());
        Assert.Throws<InvalidDataException>(()=>files.Prepare(f.Edit(false)));Assert.Null(files.PreparedPath);Assert.Null(files.PreparedSha256);Assert.Throws<InvalidDataException>(()=>files.RequirePrepared());
    }
    [Fact]public void SelectingAnotherOrInvalidBackupClearsAllPreparedAuthorization()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);files.Prepare(f.Edit());files.SelectOriginal(f.Original);
        Assert.Null(files.PreparedPath);Assert.Null(files.EditedPath);
        Assert.ThrowsAny<IOException>(()=>files.SelectOriginal(Path.Combine(f.Root,"missing","original.rom")));Assert.Null(files.OriginalPath);Assert.Null(files.PreparedPath);
    }
    [Fact]public void SaveBinIsAnExactRawCopyAndNeverOverwritesAnExistingFile()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);files.SelectOriginal(f.Original);string path=Path.Combine(f.Root,"download.bin");files.SaveBin(path);
        Assert.Equal(f.Bytes,File.ReadAllBytes(path));Assert.Throws<IOException>(()=>files.SaveBin(path));Assert.Equal(f.Bytes,File.ReadAllBytes(f.Original));
    }
    [Fact]public void MissingBackupAndUnpreparedTargetAreRejectedWithoutHardware()
    {
        using var f=new Fixture();var files=new DesktopFiles(f.Root);Assert.Throws<InvalidDataException>(()=>files.Prepare(f.Edit()));Assert.Throws<InvalidDataException>(()=>files.SaveBin(Path.Combine(f.Root,"out.bin")));Assert.Throws<InvalidDataException>(()=>files.RequirePrepared());
    }
}
