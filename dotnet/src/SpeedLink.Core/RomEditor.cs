using System.Globalization;

namespace SpeedLink.Core;

public enum TuningOperation { Set, Add, Multiply }
public enum TuningBaseline { Original, LastSave, Comparison }
public sealed record CellEdit(TuningTable Table, int Index, double Value);
public sealed record ByteChange(int Address, byte Before, byte After);
public sealed record EditTransaction(string Description, ByteChange[] Changes);

/// <summary>Owns binary snapshots and atomic byte transactions. No transport or ECU operations.</summary>
public sealed class RomEditor
{
    private readonly RomInspector inspector;
    private readonly TuningCodeGuard codeGuard;
    private readonly TuningDefinition codeDefinition;
    private readonly HashSet<TuningTable> tables;
    private readonly byte[] original;
    private byte[] current, lastSave;
    private byte[]? comparison;
    private readonly List<EditTransaction> undo = [], redo = [];
    public TuningDefinition Definition { get; }
    public string SourcePath { get; }
    public string? LastSavePath { get; private set; }
    public string? ComparisonPath { get; private set; }
    public bool IsDirty => !current.AsSpan().SequenceEqual(lastSave);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoDescription => CanUndo ? undo[^1].Description : "";
    public string RedoDescription => CanRedo ? redo[^1].Description : "";
    public event EventHandler? Changed;
    public RomEditor(byte[] image, string sourcePath, RomInspector? inspector = null, TuningCodeGuard? codeGuard = null, IReadOnlyList<TuningDefinition>? definitions = null)
    {
        this.inspector = inspector ?? new();
        this.codeGuard = codeGuard ?? new();
        var inspection = this.inspector.Inspect(image);
        Definition = (definitions ?? TuningDefinition.Known).SingleOrDefault(d => d.Matches(image)) ?? throw new InvalidDataException("No matching tuning definition for this ROM. Unknown ROMs remain readable in Connection & backup.");
        if (definitions is not null) DefinitionPolicy.ValidateEditor(Definition);
        codeDefinition = TuningDefinition.Known.Single(d => d.Calibration == Definition.Calibration);
        tables = new(Definition.Tables);
        if ((inspection.CanExport || inspection.ProfileValid) &&
            !this.codeGuard.Matches(image, codeDefinition, this.inspector.ProfileFor(image)!))
            throw new InvalidDataException("The ROM program area differs from this stock definition. Patched or extended layouts are not supported by this editor.");
        if (!inspection.CanExport) throw new InvalidDataException("ROM boot, integrity or layout does not match this stock definition. Editing is unavailable for this image.");
        SourcePath = Path.GetFullPath(sourcePath);
        original = (byte[])image.Clone(); current = (byte[])image.Clone(); lastSave = (byte[])image.Clone();
        foreach (var table in Definition.Tables)
            if (inspection.CorrectionAddress is int correction && Overlaps(table.Address, table.End, correction, correction + 4) ||
                Overlaps(table.Address, table.End, Definition.IdentityAddress, Definition.IdentityAddress + Definition.Identity.Length))
                throw new InvalidDataException("Definition overlaps protected ROM metadata.");
    }
    private static bool Overlaps(int a, int b, int c, int d) => a < d && c < b;
    public byte[] Snapshot() => (byte[])current.Clone();
    public RomInspection Inspect() => inspector.Inspect(current);
    private byte[] Baseline(TuningBaseline baseline) => baseline switch { TuningBaseline.Original => original, TuningBaseline.LastSave => lastSave, TuningBaseline.Comparison => comparison ?? throw new InvalidOperationException("Load a comparison ROM first."), _ => throw new ArgumentOutOfRangeException(nameof(baseline)) };
    public bool HasComparison => comparison is not null;
    public void LoadComparison(byte[] image, string path)
    {
        const string incompatible = "Comparison requires the same calibration and compatible stock layout.";
        if (!Definition.Matches(image)) throw new InvalidDataException(incompatible);
        var inspection = inspector.Inspect(image);
        if ((inspection.CanExport || inspection.ProfileValid) &&
            !codeGuard.Matches(image, codeDefinition, inspector.ProfileFor(image)!))
            throw new InvalidDataException("Comparison ROM has an unsupported program modification.");
        if (!inspection.CanExport) throw new InvalidDataException(incompatible);
        comparison = (byte[])image.Clone(); ComparisonPath = Path.GetFullPath(path); Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Check(TuningTable table, int index)
    {
        if (!tables.Contains(table) || index < 0 || index >= table.Count) throw new ArgumentOutOfRangeException(nameof(index));
    }
    private static ulong Raw(ReadOnlySpan<byte> bytes, bool big)
    {
        ulong value = 0; for (int i = 0; i < bytes.Length; i++) value = (value << 8) | bytes[big ? i : bytes.Length - i - 1]; return value;
    }
    private static double Decode(TuningTable table, ReadOnlySpan<byte> bytes, int index)
    {
        ulong raw = Raw(bytes, table.BigEndian);
        if (table.Bits.Length > 0) return (raw & (1UL << table.Bits[index])) == 0 ? 0 : 1;
        double value = table.Storage switch
        {
            "float" => BitConverter.Int32BitsToSingle(unchecked((int)raw)),
            "int8" => unchecked((sbyte)raw), "int16" => unchecked((short)raw), "int32" => unchecked((int)raw), _ => raw
        };
        return table.Decode(value);
    }
    public double Read(TuningTable table, int index, TuningBaseline? baseline = null)
    {
        Check(table, index); return Decode(table, (baseline.HasValue ? Baseline(baseline.Value) : current).AsSpan(table.AddressAt(index), table.Width), index);
    }
    public bool CellChanged(TuningTable table, int index, TuningBaseline baseline = TuningBaseline.Original)
    {
        Check(table, index); int at = table.AddressAt(index);
        if (table.Bits.Length > 0) return ((Raw(current.AsSpan(at, table.Width), table.BigEndian) ^ Raw(Baseline(baseline).AsSpan(at, table.Width), table.BigEndian)) & (1UL << table.Bits[index])) != 0;
        return !current.AsSpan(at, table.Width).SequenceEqual(Baseline(baseline).AsSpan(at, table.Width));
    }
    public bool TableChanged(TuningTable table, TuningBaseline baseline = TuningBaseline.Original) => Enumerable.Range(0, table.Count).Any(i => CellChanged(table, i, baseline));
    private static byte[] Encode(TuningTable table, int index, double value, ReadOnlySpan<byte> existing)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Enter a finite numeric value.");
        ulong raw;
        if (table.Bits.Length > 0)
        {
            if (value is not (0 or 1)) throw new ArgumentException("A bit control must be 0 or 1.");
            ulong mask = 1UL << table.Bits[index]; raw = (Raw(existing, table.BigEndian) & ~mask) | (value == 0 ? 0 : mask);
        }
        else
        {
            double encoded = table.Encode(value);
            if (!double.IsFinite(encoded)) throw new ArgumentException("Value cannot be encoded.");
            if (table.Storage == "float")
            {
                float rounded = (float)encoded; if (!float.IsFinite(rounded)) throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds floating-point storage.");
                raw = unchecked((uint)BitConverter.SingleToInt32Bits(rounded));
            }
            else
            {
                double rounded = Math.Round(encoded, MidpointRounding.AwayFromZero);
                double min = table.Storage.StartsWith("int", StringComparison.Ordinal) ? -Math.Pow(2, table.Width * 8 - 1) : 0;
                double max = Math.Pow(2, table.Width * 8 - (min < 0 ? 1 : 0)) - 1;
                double a = table.Decode(min), b = table.Decode(max);
                if (double.IsFinite(a) && double.IsFinite(b) && (value < Math.Min(a, b) || value > Math.Max(a, b))) throw new ArgumentOutOfRangeException(nameof(value), "Value is outside the scaled storage range.");
                if (encoded < min - 1e-8 || encoded > max + 1e-8 || rounded < min || rounded > max) throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds encoded storage range.");
                raw = unchecked((ulong)(long)rounded);
                if (table.Kind == "Switch" && !table.States.Any(s => s.Value == raw)) throw new ArgumentException("Choose a defined switch state.");
            }
        }
        var bytes = new byte[table.Width];
        for (int i = 0; i < bytes.Length; i++) bytes[table.BigEndian ? bytes.Length - i - 1 : i] = (byte)(raw >> (8 * i));
        if (!double.IsFinite(Decode(table, bytes, index))) throw new ArgumentException("Encoded value is not finite.");
        return bytes;
    }
    public void Apply(IEnumerable<CellEdit> edits, string description)
    {
        var pending = new Dictionary<int, byte>();
        foreach (var edit in edits)
        {
            Check(edit.Table, edit.Index); var t = edit.Table; int at = t.AddressAt(edit.Index);
            var old = current.AsSpan(at, t.Width).ToArray();
            for (int i = 0; i < old.Length; i++) if (pending.TryGetValue(at + i, out byte staged)) old[i] = staged;
            // Preserve noncanonical encodings when their displayed value has not changed.
            if (edit.Value == Decode(t, old, edit.Index)) continue;
            var bytes = Encode(t, edit.Index, edit.Value, old);
            for (int i = 0; i < bytes.Length; i++) pending[at + i] = bytes[i];
        }
        Commit(pending.Where(p => current[p.Key] != p.Value).Select(p => new ByteChange(p.Key, current[p.Key], p.Value)).OrderBy(p => p.Address).ToArray(), description);
    }
    private void Commit(ByteChange[] changes, string description)
    {
        if (changes.Length == 0) return;
        foreach (var edit in changes) current[edit.Address] = edit.After;
        undo.Add(new(description, changes)); if (undo.Count > 200) undo.RemoveAt(0); redo.Clear(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Undo() { if (!CanUndo) return; var item = undo[^1]; undo.RemoveAt(undo.Count - 1); foreach (var c in item.Changes) current[c.Address] = c.Before; redo.Add(item); Changed?.Invoke(this, EventArgs.Empty); }
    public void Redo() { if (!CanRedo) return; var item = redo[^1]; redo.RemoveAt(redo.Count - 1); foreach (var c in item.Changes) current[c.Address] = c.After; undo.Add(item); Changed?.Invoke(this, EventArgs.Empty); }
    public void Transform(TuningTable table, IEnumerable<int> indices, TuningOperation operation, double operand)
    {
        if (table.IsChoice) throw new InvalidOperationException("Use the switch selector for this control.");
        Apply(indices.Distinct().Select(i => new CellEdit(table, i, operation switch { TuningOperation.Set => operand, TuningOperation.Add => Read(table, i) + operand, TuningOperation.Multiply => Read(table, i) * operand, _ => throw new ArgumentOutOfRangeException(nameof(operation)) })), operation + " " + table.Name);
    }
    public void Reset(TuningTable table, IEnumerable<int> indices, TuningBaseline baseline)
    {
        var pending = new Dictionary<int, byte>(); var source = Baseline(baseline);
        foreach (int index in indices.Distinct())
        {
            Check(table, index); int at = table.AddressAt(index);
            if (table.Bits.Length > 0)
            {
                var old = current.AsSpan(at, table.Width).ToArray();
                for (int i = 0; i < old.Length; i++) if (pending.TryGetValue(at + i, out byte staged)) old[i] = staged;
                var bytes = Encode(table, index, Read(table, index, baseline), old);
                for (int i = 0; i < bytes.Length; i++) pending[at + i] = bytes[i];
            }
            else for (int i = 0; i < table.Width; i++) pending[at + i] = source[at + i];
        }
        Commit(pending.Where(p => current[p.Key] != p.Value).Select(p => new ByteChange(p.Key, current[p.Key], p.Value)).ToArray(), "Reset " + table.Name);
    }
    public void Interpolate(TuningTable table, int left, int top, int right, int bottom, string direction)
    {
        if (table.IsChoice || left < 0 || right >= table.Columns || top < 0 || bottom >= table.Rows || right < left || bottom < top) throw new ArgumentException("Select a numeric rectangle.");
        if (direction is not ("Horizontal" or "Vertical" or "2D")) throw new ArgumentException("Unknown interpolation direction.");
        bool horizontal = direction != "Vertical", vertical = direction != "Horizontal";
        if (horizontal && left == right || vertical && top == bottom) throw new ArgumentException("Select at least two endpoints in each interpolation direction.");
        static double Weight(double[] axis, int start, int end, int index)
        {
            double span = axis[end] - axis[start];
            if (span == 0 || !double.IsFinite(span)) throw new InvalidDataException("Interpolation endpoints must have different axis values.");
            return (axis[index] - axis[start]) / span;
        }
        double V(int row, int col) => Read(table, row * table.Columns + col);
        var edits = new List<CellEdit>();
        for (int row = top; row <= bottom; row++) for (int col = left; col <= right; col++)
        {
            double wx = horizontal ? Weight(table.X.Values, left, right, col) : 0;
            double wy = vertical ? Weight(table.Y.Values, top, bottom, row) : 0;
            double value = direction switch
            {
                "Horizontal" => V(row, left) * (1 - wx) + V(row, right) * wx,
                "Vertical" => V(top, col) * (1 - wy) + V(bottom, col) * wy,
                _ => (V(top, left) * (1 - wx) + V(top, right) * wx) * (1 - wy) + (V(bottom, left) * (1 - wx) + V(bottom, right) * wx) * wy
            };
            edits.Add(new(table, row * table.Columns + col, value));
        }
        Apply(edits, direction + " interpolation: " + table.Name);
    }
    public static double[][] ParseClipboard(string text)
    {
        if (text.Length > 1_000_000) throw new InvalidDataException("Clipboard is too large.");
        var rows = text.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r').Split('\t').Select(s =>
        {
            if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) throw new InvalidDataException("Paste tab-separated numeric values without axis headers.");
            return value;
        }).ToArray()).ToArray();
        if (rows.Length == 0 || rows.Any(r => r.Length != rows[0].Length)) throw new InvalidDataException("Clipboard must be a rectangle.");
        return rows;
    }
    public void PasteCurve(TuningTable table, int start, string text)
    {
        if (table.Rows != 1 || table.IsChoice) throw new ArgumentException("Select a numeric curve.");
        var rows = ParseClipboard(text);
        double[] values;
        if (rows.Length == 1 && rows[0].Length > 2) values = rows[0];
        else
        {
            if (rows[0].Length > 2) throw new InvalidDataException("Paste one value column, or matching breakpoint and value columns.");
            if (start < 0 || start > table.Count - rows.Length) throw new ArgumentException("Pasted values exceed the curve.");
            if (rows[0].Length == 2)
                for (int i = 0; i < rows.Length; i++)
                    if (Math.Abs(rows[i][0] - table.X.Values[start + i]) > 1e-10 * Math.Max(1, Math.Abs(table.X.Values[start + i])))
                        throw new InvalidDataException("Breakpoints do not match. Paste only the value column to keep the existing axis.");
            values = rows.Select(row => row[^1]).ToArray();
        }
        if (start < 0 || start > table.Count - values.Length) throw new ArgumentException("Pasted values exceed the curve.");
        Apply(values.Select((value, index) => new CellEdit(table, start + index, value)), "Paste curve: " + table.Name);
    }
    public void Paste(TuningTable table, int row, int col, string text)
    {
        if (table.IsChoice) throw new InvalidOperationException("Use the switch selector.");
        var values = ParseClipboard(text);
        if (row < 0 || col < 0 || row + values.Length > table.Rows || col + values[0].Length > table.Columns) throw new ArgumentException("Clipboard does not fit in this table.");
        Apply(values.SelectMany((r, y) => r.Select((v, x) => new CellEdit(table, (row + y) * table.Columns + col + x, v))), "Paste " + table.Name);
    }
    /// <summary>
    /// Always applies the ROM's verified checksum policy before publishing a new file.
    /// Correction and durable readback verification are mandatory, including no-edit saves.
    /// Native validation-only profiles reject invalid code without guessed repair.
    /// </summary>
    public void SaveNew(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(path, SourcePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Save to a new file; the original ROM is preserved.");
        byte[] output = inspector.CorrectedCopy(current);
        var prepared = inspector.Inspect(output);
        if (!prepared.ProfileValid || prepared.ChecksumValid != true)
            throw new InvalidDataException("ROM export requires a verified checksum, identity, boot and integrity layout.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(output); stream.Flush(true); }
            var written = File.ReadAllBytes(temp);
            var verified = inspector.Inspect(written);
            if (!written.AsSpan().SequenceEqual(output) || !verified.ProfileValid || verified.ChecksumValid != true) throw new IOException("Saved copy failed byte or integrity verification.");
            File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        var changes = Enumerable.Range(0, output.Length).Where(i => current[i] != output[i]).Select(i => new ByteChange(i, current[i], output[i])).ToArray();
        Commit(changes, "Checksum correction on save");
        lastSave = (byte[])output.Clone(); LastSavePath = path; Changed?.Invoke(this, EventArgs.Empty);
    }
}
