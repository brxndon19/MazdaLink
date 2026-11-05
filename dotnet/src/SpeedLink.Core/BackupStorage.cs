using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpeedLink.Core;

public static class CanonicalJson
{
    public static byte[] Bytes(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        var text = new StringBuilder();
        Write(document.RootElement, text);
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    private static void Quote(string value, StringBuilder text)
    {
        text.Append('"');
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' or > '~' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString()
            });
        }
        text.Append('"');
    }
    private static void Write(JsonElement value, StringBuilder text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                text.Append('{');
                bool first = true;
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                        text.Append(',');
                    first = false;
                    Quote(property.Name, text);
                    text.Append(':');
                    Write(property.Value, text);
                }
                text.Append('}');
                break;
            case JsonValueKind.Array:
                text.Append('[');
                bool firstItem = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!firstItem)
                        text.Append(',');
                    firstItem = false;
                    Write(item, text);
                }
                text.Append(']');
                break;
            case JsonValueKind.String:
                Quote(value.GetString()!, text);
                break;
            case JsonValueKind.Number:
                text.Append(value.GetRawText());
                break;
            case JsonValueKind.True:
                text.Append("true");
                break;
            case JsonValueKind.False:
                text.Append("false");
                break;
            case JsonValueKind.Null:
                text.Append("null");
                break;
            default:
                throw new InvalidDataException("Unsupported JSON value.");
        }
    }
}

public interface IDurableFile : IDisposable
{
    int Write(byte[] data); void Flush(); void Sync();
}
public interface IBackupFileSystem
{
    IDurableFile CreateNew(string path);
    byte[] Read(string path);
    bool Exists(string path);
    void Delete(string path);
    void MarkReadOnly(string path);
    void CreateDirectory(string path);
}
public sealed class BackupFileSystem : IBackupFileSystem
{
    private sealed class DurableFile(string path) : IDurableFile
    {
        private readonly FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        public int Write(byte[] data)
        {
            file.Write(data);
            return data.Length;
        }
        public void Flush() => file.Flush();
        public void Sync() => file.Flush(flushToDisk: true);
        public void Dispose() => file.Dispose();
    }
    public IDurableFile CreateNew(string path) => new DurableFile(path);
    public byte[] Read(string path) => File.ReadAllBytes(path);
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path) => File.Delete(path);
    public void MarkReadOnly(string path) => File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
public static class DurableStorage
{
    public static void Write(IDurableFile file, byte[] data)
    {
        if (file.Write(data) != data.Length)
            throw new IOException("Short disk write; operation stopped before the next request.");
        file.Flush();
        file.Sync();
    }
    public static void NewFile(IBackupFileSystem fileSystem, string path, byte[] data)
    {
        using var file = fileSystem.CreateNew(path);
        Write(file, data);
    }
    public static void NewJson(IBackupFileSystem fileSystem, string path, object value)
        => NewFile(fileSystem, path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n"));
}

public sealed class OperationJournal : IDisposable
{
    private readonly IBackupFileSystem fileSystem;
    private readonly IDurableFile file;
    private string previous = new('0', 64);
    private int sequence;
    public string Path
    {
        get;
    }
    public string PendingPath => Path + ".incomplete";
    public bool Failed
    {
        get; private set;
    }
    public bool Completed
    {
        get; private set;
    }
    public OperationJournal(string path, IBackupFileSystem? fileSystem = null)
    {
        Path = path;
        this.fileSystem = fileSystem ?? new BackupFileSystem();
        file = this.fileSystem.CreateNew(path);
        try
        {
            DurableStorage.NewFile(this.fileSystem, PendingPath, "Journal completion has not been committed.\n"u8.ToArray());
        }
        catch { Failed = true; file.Dispose(); throw; }
    }
    public void Append(string eventName, Dictionary<string, object?>? details = null)
    {
        if (Failed)
            throw new IOException("Journal storage previously failed; no further records or requests are allowed.");
        if (Completed)
            throw new IOException("Journal is already complete.");
        var fields = new Dictionary<string, object?>(details ?? []);
        if (sequence == 0)
            fields["journalVersion"] = 2;
        var row = new JsonObject
        {
            ["sequence"] = sequence,
            ["previous"] = previous,
            ["time"] = DateTimeOffset.UtcNow.ToString("O"),
            ["event"] = eventName,
            ["details"] = JsonSerializer.SerializeToNode(fields)
        };
        var hash = RomInspector.Hash(CanonicalJson.Bytes(row));
        row["hash"] = hash;
        try
        {
            DurableStorage.Write(file, [.. CanonicalJson.Bytes(row), (byte)'\n']);
            if (eventName == "completed")
            {
                fileSystem.Delete(PendingPath);
                Completed = true;
            }
        }
        catch { Failed = true; throw; }
        previous = hash;
        sequence++;
    }
    public void RecordStopped(Dictionary<string, object?> details)
    {
        if (!Failed && !Completed)
            try
            {
                Append("stopped", details);
            }
            catch (IOException) { }
    }
    public void Dispose() => file.Dispose();
}

public sealed record JournalInspection(string Path, bool Valid, bool Complete, int Records, string? Operation = null,
    string? Mode = null, string? LastEvent = null, JsonObject? LastDetails = null, bool CompletionPending = false, string? Error = null);
public sealed record LoadedBackup(byte[] Data, JsonObject Manifest);

public sealed class BackupStore
{
    public IBackupFileSystem FileSystem
    {
        get;
    }
    public BackupStore(IBackupFileSystem? fileSystem = null) => FileSystem = fileSystem ?? new BackupFileSystem();
    public (string Folder, OperationJournal Journal) CreateOperation(string vault, string mode, string operation)
    {
        if (mode is not ("hardware" or "simulator"))
            throw new InvalidDataException("Unknown storage mode.");
        var folder = System.IO.Path.Combine(vault, mode, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..10]);
        FileSystem.CreateDirectory(folder);
        var journal = new OperationJournal(System.IO.Path.Combine(folder, "journal.jsonl"), FileSystem);
        try
        {
            journal.Append("started", new()
            {
                ["mode"] = mode,
                ["operation"] = operation,
                ["version"] = typeof(BackupStore).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion
            });
        }
        catch { journal.Dispose(); throw; }
        return (folder, journal);
    }
    public JournalInspection InspectJournal(string path) => InspectJournal(path, null);
    internal JournalInspection InspectJournal(string path, Action<IReadOnlyList<JsonObject>>? observe)
    {
        var previous = new string('0', 64);
        var rows = new List<JsonObject>();
        try
        {
            var raw = FileSystem.Read(path);
            if (raw.Length == 0 || raw[^1] != (byte)'\n')
                throw new InvalidDataException("Empty or incomplete journal record.");
            var lines = new UTF8Encoding(false, true).GetString(raw).Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var row = JsonNode.Parse(lines[i]) as JsonObject ?? throw new InvalidDataException("Malformed journal record.");
                if (row["details"] is not JsonObject || row["event"]?.GetValue<string>() is null)
                    throw new InvalidDataException("Malformed journal details.");
                var hash = row["hash"]?.GetValue<string>() ?? throw new InvalidDataException("Missing journal hash.");
                row.Remove("hash");
                if (row["sequence"]?.GetValue<int>() != i || row["previous"]?.GetValue<string>() != previous)
                    throw new InvalidDataException("Journal sequence/chain mismatch.");
                if (RomInspector.Hash(CanonicalJson.Bytes(row)) != hash)
                    throw new InvalidDataException("Journal content hash mismatch.");
                previous = hash;
                rows.Add(row);
            }
            if (rows.Count == 0)
                throw new InvalidDataException("Empty journal.");
            var version = rows[0]["details"]?["journalVersion"]?.GetValue<int>();
            if (version is not (null or 1 or 2))
                throw new InvalidDataException("Unsupported journal version.");
            var pending = version == 2 && FileSystem.Exists(path + ".incomplete");
            observe?.Invoke(rows);
            return new(path, true, rows[^1]["event"]!.GetValue<string>() == "completed" && !pending, rows.Count,
                rows[0]["details"]?["operation"]?.GetValue<string>() ?? "unknown", rows[0]["details"]?["mode"]?.GetValue<string>() ?? "unknown",
                rows[^1]["event"]!.GetValue<string>(), rows[^1]["details"]!.AsObject(), pending);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return new(path, false, false, rows.Count, Error: error.Message);
        }
    }
    public IReadOnlyList<JournalInspection> Scan(string vault)
    {
        if (!Directory.Exists(vault))
            return [];
        var paths = Directory.EnumerateDirectories(vault).SelectMany(Directory.EnumerateDirectories)
            .Select(folder => System.IO.Path.Combine(folder, "journal.jsonl")).Where(File.Exists).OrderDescending(StringComparer.Ordinal);
        return paths.Select(InspectJournal).ToArray();
    }
    public string Seal(string folder, byte[] data, EcuIdentity identity, RomInspection inspection, string firstHash, string secondHash, string mode)
    {
        var hash = RomInspector.Hash(data);
        if (hash != firstHash || hash != secondHash)
            throw new InvalidDataException("The two read passes do not match.");
        var path = System.IO.Path.Combine(folder, "rom.bin");
        DurableStorage.NewFile(FileSystem, path, data);
        DurableStorage.NewJson(FileSystem, System.IO.Path.Combine(folder, "backup.json"), new
        {
            formatVersion = 1,
            created = DateTimeOffset.UtcNow.ToString("O"),
            mode,
            simulated = mode == "simulator",
            romFile = "rom.bin",
            size = data.Length,
            sha256 = hash,
            firstPassSHA256 = firstHash,
            secondPassSHA256 = secondHash,
            identity = identity.ToManifest(),
            inspection = inspection.ToManifest(),
            vehicleMatchVerified = false,
            factoryProvenanceVerified = false
        });
        foreach (var name in new[] { "rom.bin", "pass1.read", "pass2.read", "backup.json" })
            FileSystem.MarkReadOnly(System.IO.Path.Combine(folder, name));
        return path;
    }
    public LoadedBackup Load(string path, string? requiredMode = null)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var manifest = JsonNode.Parse(FileSystem.Read(System.IO.Path.Combine(folder, "backup.json")))?.AsObject() ?? throw new InvalidDataException("Missing manifest.");
        var mode = manifest["mode"]?.GetValue<string>();
        if (manifest["formatVersion"]?.GetValue<int>() != 1 || mode is not ("hardware" or "simulator") || manifest["simulated"]?.GetValue<bool>() != (mode == "simulator"))
            throw new InvalidDataException("Unrecognized or inconsistent backup manifest format.");
        var journal = InspectJournal(System.IO.Path.Combine(folder, "journal.jsonl"));
        if (!journal.Valid || !journal.Complete || journal.Operation != "read_backup")
            throw new InvalidDataException("Backup requires a complete, intact read journal.");
        var hash = manifest["sha256"]?.GetValue<string>();
        if (journal.Mode != mode || journal.LastDetails?["sha256"]?.GetValue<string>() != hash)
            throw new InvalidDataException("Manifest does not match the completed read journal.");
        if (System.IO.Path.GetFileName(path) != "rom.bin" || manifest["romFile"]?.GetValue<string>() != "rom.bin")
            throw new InvalidDataException("Select rom.bin from the backup folder.");
        var data = FileSystem.Read(path);
        var actual = RomInspector.Hash(data);
        if (data.Length != RomInspector.RomSize || manifest["size"]?.GetValue<int>() != data.Length || hash != actual
            || manifest["firstPassSHA256"]?.GetValue<string>() != actual || manifest["secondPassSHA256"]?.GetValue<string>() != actual)
            throw new InvalidDataException("Backup no longer matches its verified read manifest.");
        foreach (var name in new[] { "pass1.read", "pass2.read" })
            if (RomInspector.Hash(FileSystem.Read(System.IO.Path.Combine(folder, name))) != actual)
                throw new InvalidDataException("Original capture no longer matches backup.");
        if (requiredMode is not null && mode != requiredMode)
            throw new InvalidDataException("This operation requires a " + requiredMode + " backup.");
        return new(data, manifest);
    }
}
