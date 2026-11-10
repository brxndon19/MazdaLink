using System.Text.Json;

namespace SpeedLink.Core;

/// <summary>Presentation only: never contains ROM paths, bytes, or tuning values.</summary>
public sealed record MapPanePreferences
{
    public string Key { get; init; } = "";
    public double Zoom { get; init; } = 1;
    public bool AutoFit { get; init; } = true;
    public bool? FitTable { get; init; }
    public int View { get; init; } = 4;
    public int Baseline { get; init; }
    public int[] Selection { get; init; } = [];
    public double HorizontalOffset { get; init; }
    public double VerticalOffset { get; init; }
    public double? PreviewWidth { get; init; }
    public double TableFraction { get; init; } = .6;
    public bool Floating { get; init; }
    public bool Pinned { get; init; } = true;
    public PanelBounds Bounds { get; init; } = new(Width: 840, Height: 650);
    public MapDockBounds? DockBounds { get; init; }
    public int ZOrder { get; init; }
    public double Yaw { get; init; } = 40;
    public double Elevation { get; init; } = 32;
    public double Distance { get; init; } = 5;
    public double CurveStart { get; init; }
    public double CurveEnd { get; init; } = 1;
    public MapPanePreferences Validate() => this with
    {
        Key = Key is { Length: <= 320 } ? Key : "",
        Zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, .85, 1.5) : 1,
        View = Math.Clamp(View, 0, 4), Baseline = Math.Clamp(Baseline, 0, 1),
        Selection = (Selection ?? []).Where(i => i >= 0).Distinct().Take(4096).ToArray(),
        HorizontalOffset = Offset(HorizontalOffset), VerticalOffset = Offset(VerticalOffset),
        PreviewWidth = PreviewWidth is double width && double.IsFinite(width) ? Math.Clamp(width, 160, 800) : null,
        TableFraction = double.IsFinite(TableFraction) ? Math.Clamp(TableFraction, .2, .8) : .6,
        Pinned = Pinned || Floating,
        Bounds = new EditorLayoutPreferences { GraphBounds = Bounds }.Validate().GraphBounds,
        DockBounds = DockBounds?.Validate(),
        ZOrder = Math.Clamp(ZOrder, 0, 23),
        Yaw = double.IsFinite(Yaw) ? Yaw % 360 : 40,
        Elevation = Finite(Elevation, 10, 80, 32), Distance = Finite(Distance, 2.6, 12, 5),
        CurveStart = Finite(CurveStart, 0, .98, 0), CurveEnd = Finite(CurveEnd, .02, 1, 1)
    };
    private static double Offset(double value) => Finite(value, 0, 1_000_000, 0);
    private static double Finite(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public record MapPagePreferences
{
    public const int MaxNameLength = 36;
    public string Name { get; init; } = "Workspace";
    public MapPanePreferences[] Maps { get; init; } = [];
    public string Active { get; init; } = "";
    public string Expanded { get; init; } = "";
    public int Columns { get; init; } = 2;
    public double ColumnFraction { get; init; } = .5;
    public double RowFraction { get; init; } = .5;
    public double[] RowWeights { get; init; } = [];
    public double[] ScalarColumnWidths { get; init; } = [];
    public static bool ValidName(string? name) => name is { Length: > 0 and <= MaxNameLength } && !name.Any(char.IsControl);

    internal MapPagePreferences ValidatePage()
    {
        var panes = (Maps ?? []).Where(m => m is not null).Select(m => m.Validate()).Where(m => m.Key.Length > 0).DistinctBy(m => m.Key).Take(24).ToArray();
        var preview = panes.FirstOrDefault(m => !m.Pinned);
        return this with
        {
            Name = Name?.Trim() ?? "",
            Maps = panes.Select(m => !m.Pinned && !ReferenceEquals(m, preview) ? m with { Pinned = true } : m).ToArray(),
            Active = Active is { Length: <= 320 } ? Active : "",
            Expanded = Expanded is { Length: <= 320 } ? Expanded : "",
            Columns = Math.Clamp(Columns, 1, 2),
            ColumnFraction = double.IsFinite(ColumnFraction) ? Math.Clamp(ColumnFraction, .2, .8) : .5,
            RowFraction = double.IsFinite(RowFraction) ? Math.Clamp(RowFraction, .2, .8) : .5,
            RowWeights = (RowWeights ?? []).Take(24).Select(v => double.IsFinite(v) ? Math.Clamp(v, .01, 1) : 1).ToArray(),
            ScalarColumnWidths = (ScalarColumnWidths ?? []).Take(6).Select(v => double.IsFinite(v) ? Math.Clamp(v, 220, 800) : 300).ToArray()
        };
    }
}

public sealed record MapWorkspacePreferences : MapPagePreferences
{
    public const int MaxPages = 12;
    private const int MaxFileBytes = 4 * 1024 * 1024;
    public string Calibration { get; init; } = "";
    public MapPagePreferences[] Pages { get; init; } = [];
    public string SelectedPage { get; init; } = "";

    public MapWorkspacePreferences Validate()
    {
        var pages = (Pages ?? []).Where(p => p is not null).Select(p => p.ValidatePage()).Where(p => ValidName(p.Name))
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(MaxPages).ToArray();
        var legacyPage = (MapWorkspacePreferences)ValidatePage();
        return legacyPage with
        {
            Calibration = Calibration is { Length: > 0 and <= 32 } && Calibration.All(char.IsAsciiLetterOrDigit) ? Calibration : "",
            Name = ValidName(legacyPage.Name) ? legacyPage.Name : "Workspace",
            Pages = pages,
            SelectedPage = pages.FirstOrDefault(p => string.Equals(p.Name, SelectedPage, StringComparison.OrdinalIgnoreCase))?.Name ?? ""
        };
    }
    public static MapWorkspacePreferences[] Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaxFileBytes) return [];
            return (JsonSerializer.Deserialize<MapWorkspacePreferences[]>(stream, new JsonSerializerOptions { MaxDepth = 12 }) ?? [])
                .Where(p => p is not null).Select(p => p.Validate()).Where(p => p.Calibration.Length > 0).DistinctBy(p => p.Calibration).Take(32).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public static void Save(string path, IEnumerable<MapWorkspacePreferences> profiles)
    {
        var valid = profiles.Select(p => p.Validate()).Where(p => p.Calibration.Length > 0).DistinctBy(p => p.Calibration).Take(32).ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(valid);
        if (bytes.Length > MaxFileBytes) throw new IOException("Map layout is too large to save.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(pending, path, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}

/// <summary>Fits cell spacing around measured text; never chooses a smaller font.</summary>
public readonly record struct ReadableTableFit(double ColumnWidth, double RowHeight, bool Fits)
{
    public static ReadableTableFit Calculate(double width, double height, int columns, int rows, double headerWidth, double headerHeight, double measuredTextWidth, double fontSize, double zoom, bool autoFit)
    {
        if (columns < 1 || rows < 1 || !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(headerWidth) || !double.IsFinite(headerHeight) || !double.IsFinite(zoom) || zoom < .85 || zoom > 1.5 || !double.IsFinite(fontSize) || fontSize < 11 || !double.IsFinite(measuredTextWidth) || measuredTextWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(columns));
        double minWidth = Math.Ceiling(Math.Max(44 * zoom, measuredTextWidth + 14 * zoom));
        double minHeight = Math.Ceiling(Math.Max(22 * zoom, fontSize + 10));
        double cell = autoFit ? Math.Max(minWidth, Math.Min(112 * zoom, Math.Floor((Math.Max(0, width) - headerWidth - 4) / columns))) : Math.Max(minWidth, 70 * zoom);
        double row = autoFit ? Math.Max(minHeight, Math.Min(28 * zoom, Math.Floor((Math.Max(0, height) - headerHeight - 4) / rows))) : Math.Max(minHeight, 25 * zoom);
        return new(cell, row, cell * columns + headerWidth + 4 <= width && row * rows + headerHeight + 4 <= height);
    }
}
