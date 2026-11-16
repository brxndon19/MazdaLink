namespace SpeedLink.Core;

/// <summary>Exports a verified raw backup without altering any captured byte, including checksums.</summary>
public static class BackupExport
{
    public static string SaveNew(string backupPath, string destination)
    {
        var backup = new BackupStore().Load(backupPath);
        string output = Path.GetFullPath(destination);
        string partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(backup.Data);
                file.Flush(flushToDisk: true);
            }
            if (!File.ReadAllBytes(partial).AsSpan().SequenceEqual(backup.Data))
                throw new IOException("The exported backup failed disk readback verification.");
            File.Move(partial, output, overwrite: false);
            return output;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
