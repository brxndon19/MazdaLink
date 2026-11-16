using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class BackupExportTests
{
    [Fact]
    public void ExportsExactRawBytesAndKeepsTheCompleteBackup()
    {
        using var folder = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var result = new ReadWorkflow().ReadBackup(ecu, ReadWorkflow.Identify(ecu), folder.Path);
        string output = Path.Combine(folder.Path, "original.rom");
        BackupExport.SaveNew(result.Path, output);
        Assert.Equal(Fixture.Rom, File.ReadAllBytes(output));
        Assert.Equal(Fixture.Rom, new BackupStore().Load(result.Path).Data);
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Throws<IOException>(() => BackupExport.SaveNew(result.Path, output));
        Assert.Equal(Fixture.Rom, File.ReadAllBytes(output));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void DamagedCaptureCannotBeExported()
    {
        using var folder = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var result = new ReadWorkflow().ReadBackup(ecu, ReadWorkflow.Identify(ecu), folder.Path);
        string capture = Path.Combine(Path.GetDirectoryName(result.Path)!, "pass1.read");
        File.SetAttributes(capture, FileAttributes.Normal);
        File.WriteAllBytes(capture, new byte[RomInspector.RomSize]);
        string output = Path.Combine(folder.Path, "bad.rom");
        Assert.Throws<InvalidDataException>(() => BackupExport.SaveNew(result.Path, output));
        Assert.False(File.Exists(output));
    }
}
