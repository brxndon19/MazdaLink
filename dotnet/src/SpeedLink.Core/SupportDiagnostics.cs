using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace SpeedLink.Core;

public interface IDiagnosticSink
{
    void Record(string kind, object? details = null);
    void RegisterSecret(string? value);
    void AttachJournal(string path) { }
}

public sealed class DiagnosticRedactor
{
    private readonly HashSet<string> secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    public void Register(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4) return;
        lock (gate) { secrets.Add(value); secrets.Add(Convert.ToHexString(Encoding.UTF8.GetBytes(value))); }
    }
    public string Text(string text)
    {
        lock (gate) foreach (string secret in secrets.OrderByDescending(s => s.Length)) text = text.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"\b[A-HJ-NPR-Z0-9]{17}\b", "[redacted VIN]", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"\b[A-Z][A-Z0-9]{2,5}-?[0-9]{6}\b", "[redacted chassis ID]", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?i)\b(SN|serial(?:\s+number)?)\s*[:=]\s*[A-Za-z0-9_-]+", "$1:[redacted]");
        text = Regex.Replace(text, @"(?i)\b[A-Z]:[\\/][^\r\n\""<>|]*", "[redacted path]");
        text = Regex.Replace(text, @"(?:\\\\|/home/|/Users/)[^\r\n\""<>|]*", "[redacted path]");
        return text;
    }
    public JsonNode? Clean(object? details)
    {
        var node = JsonSerializer.SerializeToNode(details);
        return Visit(node);
    }
    private JsonNode? Visit(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(p => p.Key).ToArray())
                obj[key] = Regex.IsMatch(key, "^(vin|chassisId|vehicleIdentifier|serial|serialNumber|username|userProfile|accessToken|authorization|driverPath|path)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    ? JsonValue.Create("[redacted]") : Visit(obj[key]);
        }
        else if (node is JsonArray array)
            for (int i = 0; i < array.Count; i++) array[i] = Visit(array[i]);
        else if (node is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(Text(text));
        return node;
    }
}

public sealed record DiagnosticLimits(int SegmentBytes = 2 * 1024 * 1024, int Segments = 4, int Sessions = 10, int QueueCapacity = 4096);

/// <summary>Bounded, asynchronous, privacy-filtered diagnostics. Never changes ECU requests.</summary>
public sealed class DiagnosticSession : IDiagnosticSink, IAsyncDisposable
{
    private sealed record Item(string? Line, bool Important = false, TaskCompletionSource? Barrier = null);
    private readonly Channel<Item> queue;
    private readonly Task writer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset origin = DateTimeOffset.UtcNow;
    private readonly object gate = new();
    private readonly JsonObject metadata;
    private readonly DiagnosticLimits limits;
    private long sequence, dropped;
    private bool disposed;
    public DiagnosticRedactor Redactor { get; } = new();
    public string Folder { get; }
    public string? StorageError { get; private set; }
    public long DroppedEvents => Interlocked.Read(ref dropped);
    public DiagnosticSession(string root, string version, string build, DiagnosticLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.SegmentBytes < 256 || this.limits.Segments is < 1 or > 16 || this.limits.Sessions is < 1 or > 100 || this.limits.QueueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); RejectLink(root);
        Folder = Path.Combine(root, "session-" + origin.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        metadata = new() { ["schemaVersion"] = 1, ["appVersion"] = version, ["build"] = build, ["startedUtc"] = origin.ToString("O"),
            ["windows"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription, ["processBits"] = IntPtr.Size * 8,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, ["status"] = "Recording or interrupted",
            ["requestCanId"] = "7E0", ["responseCanId"] = "7E8", ["canBitrate"] = 500000,
            ["firmwarePayloadsIncluded"] = false, ["securityAccessIncluded"] = true, ["personalInformationRedacted"] = true };
        queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(this.limits.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        SaveMetadata(); Prune(root);
        writer = Task.Run(WriteLoop);
        Record("session.started");
    }
    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Diagnostic storage cannot use a linked file or directory.");
    }
    public void RegisterSecret(string? value) => Redactor.Register(value);
    public void AttachJournal(string path)
    {
        try
        {
            string vault = Path.GetDirectoryName(Path.GetDirectoryName(Folder)!)!;
            string relative = Path.GetRelativePath(vault, Path.GetFullPath(path)).Replace('\\', '/');
            if (!Regex.IsMatch(relative, @"^hardware/[A-Za-z0-9-]+/journal\.jsonl$", RegexOptions.CultureInvariant)) return;
            lock (gate) metadata["readJournal"] = relative;
            Record("read.journal_attached");
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { StorageError = Redactor.Text(error.Message); }
    }
    public void Record(string kind, object? details = null)
    {
        if (disposed) return;
        try
        {
            var clean = Redactor.Clean(details);
            lock (gate)
            {
                var row = new JsonObject { ["sequence"] = ++sequence, ["timeUtc"] = (origin + clock.Elapsed).ToString("O"),
                    ["elapsedMs"] = clock.Elapsed.TotalMilliseconds, ["event"] = kind, ["details"] = clean };
                metadata["lastEvent"] = kind;
                if (kind is "adapter.ready" or "connection.ready") metadata["adapter"] = clean?["adapter"]?.DeepClone() ?? clean?["Adapter"]?.DeepClone();
                if (kind == "connection.ready") metadata["calibration"] = clean?["Calibration"]?.DeepClone();
                if (kind == "exchange.started") metadata["lastRequest"] = clean?.DeepClone();
                if (kind == "exchange.negative_response") metadata["lastNegativeResponse"] = clean?.DeepClone();
                bool important = kind.Contains("error", StringComparison.Ordinal) || kind is "session.closed" or "connection.closed" or "connection.ready";
                if (kind.Contains("error", StringComparison.Ordinal)) { metadata["lastFailure"] = clean?.DeepClone(); metadata["status"] = "Failed"; }
                if (kind == "connection.closed" && metadata["status"]?.GetValue<string>() != "Failed") metadata["status"] = "Disconnected";
                if (kind == "session.closed" && metadata["status"]?.GetValue<string>() != "Failed") metadata["status"] = "Closed";
                if (!queue.Writer.TryWrite(new(row.ToJsonString(), important))) Interlocked.Increment(ref dropped);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { StorageError = Redactor.Text(error.Message); }
    }
    public void Error(string kind, Exception error) => Record(kind, new { type = error.GetType().FullName, message = error.Message, stack = new StackTrace(error, false).ToString() });
    private void SaveMetadata()
    {
        JsonObject copy;
        lock (gate) { copy = (JsonObject)metadata.DeepClone(); copy["droppedEvents"] = DroppedEvents; copy["storageError"] = StorageError; copy["segmentBytes"] = limits.SegmentBytes; copy["maxSegments"] = limits.Segments; }
        string temporary = Path.Combine(Folder, "session.tmp");
        File.WriteAllText(temporary, copy.ToJsonString(new() { WriteIndented = true }));
        File.Move(temporary, Path.Combine(Folder, "session.json"), true);
    }
    private async Task WriteLoop()
    {
        StreamWriter? output = null; int segment = 0; long bytes = 0; var sinceSave = Stopwatch.StartNew();
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync())
            {
                if (item.Barrier is not null)
                {
                    try { if (output is not null) await output.FlushAsync(); SaveMetadata(); item.Barrier.TrySetResult(); }
                    catch (Exception error)
                    {
                        // Publish failure state before waking a caller that may export diagnostics immediately.
                        StorageError = Redactor.Text(error.Message);
                        item.Barrier.TrySetException(new IOException("Diagnostic storage failed: " + StorageError, error));
                        throw;
                    }
                    continue;
                }
                int size = Encoding.UTF8.GetByteCount(item.Line!) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                if (output is null || bytes + size > limits.SegmentBytes)
                {
                    if (output is not null) await output.DisposeAsync();
                    string file = Path.Combine(Folder, $"events-{++segment:D6}.jsonl");
                    output = new(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true }; bytes = 0;
                    if (segment > limits.Segments) File.Delete(Path.Combine(Folder, $"events-{segment - limits.Segments:D6}.jsonl"));
                }
                if (size > limits.SegmentBytes) { Interlocked.Increment(ref dropped); continue; }
                await output.WriteLineAsync(item.Line); bytes += size;
                if (item.Important || sinceSave.ElapsedMilliseconds >= 1000) { SaveMetadata(); sinceSave.Restart(); }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StorageError = Redactor.Text(error.Message); queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var item)) item.Barrier?.TrySetException(new IOException("Diagnostic storage failed."));
        }
        finally
        {
            try { if (output is not null) await output.DisposeAsync(); SaveMetadata(); }
            catch (Exception error) when (error is not OutOfMemoryException) { StorageError = Redactor.Text(error.Message); }
        }
    }
    public async Task FlushAsync()
    {
        if (disposed) { await writer; return; }
        if (writer.IsCompleted) { if (StorageError is not null) throw new IOException("Diagnostic storage failed: " + StorageError); return; }
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await queue.Writer.WriteAsync(new(null, Barrier: barrier));
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (ChannelClosedException) { throw new IOException("Diagnostic storage failed: " + StorageError); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        Record("session.closed"); disposed = true; queue.Writer.TryComplete(); await writer;
    }
    private void Prune(string root)
    {
        foreach (string folder in Directory.EnumerateDirectories(root, "session-*").OrderDescending(StringComparer.Ordinal).Skip(limits.Sessions))
        {
            try
            {
                if (folder == Folder || !Regex.IsMatch(Path.GetFileName(folder), @"^session-[A-Za-z0-9-]+$")) continue;
                RejectLink(folder);
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    if (name is not ("session.json" or "session.tmp") && !Regex.IsMatch(name, @"^events-\d{6}\.jsonl$")) continue;
                    RejectLink(file); File.Delete(file);
                }
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StorageError = Redactor.Text(error.Message); }
        }
    }
}

public static class SupportReport
{
    public static IReadOnlyList<string> Sessions(string root) => !Directory.Exists(root) ? [] : Directory.EnumerateDirectories(root, "session-*")
        .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(p, "session.json")))
        .OrderDescending(StringComparer.Ordinal).ToArray();
    public static JsonObject Metadata(string folder)
    {
        DiagnosticSession.RejectLink(folder); string path = Path.Combine(folder, "session.json"); DiagnosticSession.RejectLink(path);
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Diagnostic metadata exceeds the size limit.");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid diagnostic session.");
    }
    public static string Summary(JsonObject metadata) => "SpeedLink support report\n" +
        $"App: {metadata["appVersion"]}\nBuild: {metadata["build"]}\nStarted: {metadata["startedUtc"]}\nStatus: {metadata["status"]}\n" +
        $"Adapter: {metadata["adapter"] ?? "See events"}\nCalibration: {metadata["calibration"] ?? "Unavailable"}\n" +
        $"Last request: {metadata["lastRequest"]?["stage"] ?? "None"}\nRequest bytes: {metadata["lastRequest"]?["requestHex"] ?? "None"}\n" +
        $"Last failure: {metadata["lastFailure"]?["message"] ?? metadata["lastFailure"]?["error"] ?? "None"}\nDropped events: {metadata["droppedEvents"] ?? 0}\nStorage error: {metadata["storageError"] ?? "None"}\n" +
        "\nFactory security-access requests and seed/key replies are included. Personal information is redacted.\nROM bytes stay in the backup files and are excluded from this report.\nCapture is a bounded rolling history; older events may have rotated out.\nThe read journal is a sanitized diagnostic copy; use the private original for integrity validation.\n";
    public static void Export(string folder, string destination)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full)) throw new IOException("Choose a new support-report filename; existing files are preserved.");
        string temporary = full + ".partial-" + Guid.NewGuid().ToString("N");
        try { WriteArchive(folder, temporary); File.Move(temporary, full); }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }
    private static void WriteArchive(string folder, string destination)
    {
        var redactor = new DiagnosticRedactor(); var metadata = Metadata(folder); string full = Path.GetFullPath(destination);
        string vault = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(folder))!)!;
        using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Add(string name, string content) { using var entry = new StreamWriter(zip.CreateEntry(name).Open()); entry.Write(content); }
        Add("summary.txt", redactor.Text(Summary(metadata)));
        Add("environment.json", redactor.Clean(metadata)!.ToJsonString(new() { WriteIndented = true }));
        int segments = 0;
        foreach (string source in Directory.EnumerateFiles(folder, "events-*.jsonl").Order(StringComparer.Ordinal))
        {
            if (!Regex.IsMatch(Path.GetFileName(source), @"^events-\d{6}\.jsonl$") || ++segments > 16) throw new InvalidDataException("Invalid diagnostic segment set.");
            DiagnosticSession.RejectLink(source);
            if (new FileInfo(source).Length > 2 * 1024 * 1024 + 4096) throw new InvalidDataException("Diagnostic segment exceeds the size limit.");
            using var reader = new StreamReader(new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            using var target = new StreamWriter(zip.CreateEntry(Path.GetFileName(source)).Open());
            while (reader.ReadLine() is { } line)
            {
                try { var node = JsonNode.Parse(line); target.WriteLine(redactor.Clean(node)?.ToJsonString()); }
                catch (JsonException) { target.WriteLine("{\"event\":\"incomplete_record\"}"); }
            }
        }
        string? relative = metadata["readJournal"]?.GetValue<string>();
        if (relative is not null && Regex.IsMatch(relative, @"^hardware/[A-Za-z0-9-]+/journal\.jsonl$"))
        {
            string path = Path.Combine(vault, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                DiagnosticSession.RejectLink(Path.Combine(vault, "hardware")); DiagnosticSession.RejectLink(Path.GetDirectoryName(path)!); DiagnosticSession.RejectLink(path);
                if (new FileInfo(path).Length <= 4 * 1024 * 1024)
                {
                    var lines = new StringBuilder();
                    foreach (string line in File.ReadLines(path))
                    {
                        try { var row = JsonNode.Parse(line)!.AsObject(); row.Remove("hash"); row.Remove("previous"); lines.AppendLine(redactor.Clean(row)!.ToJsonString()); }
                        catch (Exception error) when (error is JsonException or InvalidOperationException or NullReferenceException) { lines.AppendLine("{\"event\":\"incomplete_record\"}"); }
                    }
                    Add("read-journal.redacted.jsonl", lines.ToString());
                }
            }
        }
    }
}
