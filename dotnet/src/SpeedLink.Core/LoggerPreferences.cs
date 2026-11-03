using System.Text.Json;

namespace SpeedLink.Core;

public sealed record LoggerChannelPreference(string Id, bool Selected, string Unit, bool? Plotted = null);

/// <summary>Local display choices only. A saved choice never establishes an ECU or ROM binding.</summary>
public sealed class LoggerPreferences
{
    public int Version { get; set; } = 3;
    public int Target { get; set; } = 0; // Zero means maximum acquisition rate, with no scan delay.
    public string Stoich { get; set; } = "";
    public bool Record { get; set; } = true;
    public string? ActiveCalibration { get; set; }
    public Dictionary<string, LoggerChannelPreference[]> Profiles { get; set; } = new(StringComparer.Ordinal);
    // Preserve all version-one IDs until their profiles have been selected, including
    // IDs absent from the startup profile. Profile-specific choices take precedence.
    public LoggerChannelPreference[] Channels { get; set; } = [];
    private static readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static LoggerPreferences Load(string path)
    {
        if (!File.Exists(path)) return new();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 131072) throw new InvalidDataException("Logger settings file is too large.");
        var value = JsonSerializer.Deserialize<LoggerPreferences>(input, options) ?? throw new InvalidDataException("Invalid logger settings.");
        if (value.Version is not (1 or 2 or 3) || value.Stoich is null || value.Stoich.Length > 128 || value.Profiles is null || value.Profiles.Count > 32 || value.Channels is null)
            throw new InvalidDataException("Invalid logger settings version or limits.");
        static void Validate(LoggerChannelPreference[] rows)
        {
            if (rows.Length > 128 || rows.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 80 || r.Unit is null || r.Unit.Length > 80) || rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                throw new InvalidDataException("Invalid logger channel settings.");
        }
        Validate(value.Channels);
        foreach (var (id, rows) in value.Profiles)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || rows is null) throw new InvalidDataException("Invalid logger profile settings.");
            Validate(rows);
        }
        if (value.Version < 3) value.Target = 0; // Earlier versions saved the old default cap without distinguishing an explicit limit.
        value.Version = 3;
        if (value.Target is not (0 or 1 or 2 or 5 or 10 or 20)) value.Target = 0;
        return value;
    }

    public IReadOnlyList<LoggerChannelPreference> ForProfile(string profile) => Profiles.TryGetValue(profile, out var rows) ? rows : Channels;

    public void Save(string path)
    {
        string full = Path.GetFullPath(path), pending = full + ".pending-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        try
        {
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, this, options); output.Flush(true);
            }
            File.Move(pending, full, true);
        }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
