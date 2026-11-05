using System.IO;
using System.Text;
using System.Text.Json;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

// The catalogue stores locations only. The existing backup and journal checks
// still validate every file in the selected backup before it can be used.
internal sealed class FlashBackupLibrary(string dataFolder, string sourcesFolder)
{
    internal void Remember(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Directory.CreateDirectory(sourcesFolder);
        string path = Path.Combine(sourcesFolder, RomInspector.Hash(Encoding.UTF8.GetBytes(folder.ToUpperInvariant())) + ".path");
        if (File.Exists(path)) return;
        string temp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, folder);
            try { File.Move(temp, path); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally { File.Delete(temp); }
    }

    internal IEnumerable<string> Candidates(string mode, EcuIdentity? identity, string? backupHash, string? preferredRom = null)
    {
        if (identity is not null && (string.IsNullOrWhiteSpace(identity.VehicleIdentifier) ||
            string.IsNullOrWhiteSpace(identity.Calibration))) yield break;
        var folders = Roots().SelectMany(root =>
            new[] { root }.Concat(Directories(root))
                .Concat(Directories(Path.Combine(root, mode)))
                .Concat(Directories(Path.Combine(root, "data", mode))))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var matches = new List<(string Path, DateTimeOffset Created)>();
        foreach (string folder in folders)
        {
            var candidate = ReadCandidate(folder, mode, identity, backupHash);
            if (candidate.HasValue) matches.Add(candidate.Value);
        }
        foreach (var candidate in matches.OrderBy(c => !string.Equals(c.Path, preferredRom, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Created)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
            yield return candidate.Path;
    }

    internal IEnumerable<string> InterruptedJournals(string vault, string mode)
        => new[] { vault }.Concat(Roots().SelectMany(root => new[] { Path.Combine(root, "flashing"), Path.Combine(root, "data", "flashing") }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root => Directories(Path.Combine(root, mode)))
            .Select(folder => Path.Combine(folder, "journal.jsonl")).Where(File.Exists).OrderDescending(StringComparer.Ordinal);

    private IEnumerable<string> Roots()
    {
        yield return dataFolder;
        string? application = Path.GetDirectoryName(dataFolder);
        if (application is not null && Path.GetDirectoryName(application) is string parent)
            foreach (string sibling in Directories(parent, "SpeedLink*").Concat(Directories(application, "SpeedLink*")))
            {
                string legacy = Path.Combine(sibling, "data");
                if (Directory.Exists(legacy)) yield return legacy;
            }
        string[] records;
        try { records = Directory.GetFiles(sourcesFolder, "*.path"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
        foreach (string record in records)
        {
            string folder;
            try { folder = File.ReadAllText(record); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            if (Path.IsPathFullyQualified(folder) && Directory.Exists(folder)) yield return folder;
        }
    }

    private static string[] Directories(string folder, string pattern = "*")
    {
        try { return Directory.GetDirectories(folder, pattern); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static (string Path, DateTimeOffset Created)? ReadCandidate(string folder, string mode, EcuIdentity? identity, string? hash)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(folder, "backup.json"));
            if (stream.Length > 64 * 1024) return null;
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            if (Field(root, "mode") != mode || hash is not null && Field(root, "sha256") != hash) return null;
            if (identity is not null)
            {
                if (!root.TryGetProperty("identity", out var saved) ||
                    Field(saved, "vin") != identity.Vin || Field(saved, "chassisId") != identity.ChassisId)
                    return null;
                string? calibration = Field(saved, "calibration");
                if (!RomInspector.KnownProfiles.Any(p => p.Family == "Gen1" &&
                    (p.Identity == calibration || p.Calibration == calibration) &&
                    (p.Identity == identity.Calibration || p.Calibration == identity.Calibration)))
                    return null;
            }
            return (Path.Combine(folder, "rom.bin"),
                DateTimeOffset.TryParse(Field(root, "created"), out var created) ? created : DateTimeOffset.MaxValue);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }

        static string? Field(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
                ? field.GetString() : null;
    }
}
