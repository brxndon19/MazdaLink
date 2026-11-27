namespace MazdaLink.Core;

/// <summary>File selection and mandatory preparation shared by the desktop interface.
/// No hardware capability is held here; the selected target cannot silently change after review.</summary>
public sealed class DesktopFiles(string dataRoot)
{
    public string? OriginalPath { get; private set; }
    public string? Calibration { get; private set; }
    public string? EditedPath { get; private set; }
    public string? PreparedPath { get; private set; }
    public string? PreparedSha256 { get; private set; }
    public int CalibrationChanges { get; private set; }
    public void SelectOriginal(string path)
    {
        ClearPrepared();OriginalPath=null;Calibration=null;var backup=RomStorage.LoadVerifiedBackup(path);OriginalPath=Path.GetFullPath(path);Calibration=backup.ReportedCalibration;
    }
    public string Read(IReadTransport link,Action<int,int>? progress=null,CancellationToken cancel=default)
    {
        ClearPrepared();OriginalPath=null;Calibration=null;
        string path=new ReadWorkflow().Capture(link,dataRoot,progress,cancel);SelectOriginal(path);return path;
    }
    public void SaveBin(string destination)
    {
        if(OriginalPath is null)throw new InvalidDataException("Read the ECU or choose a complete original backup first.");
        RomStorage.Export(OriginalPath,destination);
    }
    public string Prepare(string edited)
    {
        ClearPrepared();
        if(OriginalPath is null)throw new InvalidDataException("Choose the original backup before loading an edited BIN.");
        string folder=Path.Combine(Path.GetFullPath(dataRoot),"prepared");Directory.CreateDirectory(folder);
        string destination=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".bin");
        RomEditor.SaveNew(OriginalPath,edited,destination);
        var changes=RomEditor.Validate(OriginalPath,destination);
        PreparedSha256=RomStorage.Hash(File.ReadAllBytes(destination));CalibrationChanges=changes.Calibration;
        EditedPath=Path.GetFullPath(edited);PreparedPath=destination;return destination;
    }
    public string RequirePrepared()
    {
        if(OriginalPath is null||PreparedPath is null||PreparedSha256 is null)throw new InvalidDataException("Load and check an edited BIN first.");
        RomEditor.Validate(OriginalPath,PreparedPath);
        if(RomStorage.Hash(File.ReadAllBytes(PreparedPath))!=PreparedSha256)throw new InvalidDataException("The prepared copy changed after review. Load and check the edited file again.");
        return PreparedPath;
    }
    private void ClearPrepared(){EditedPath=null;PreparedPath=null;PreparedSha256=null;CalibrationChanges=0;}
}
