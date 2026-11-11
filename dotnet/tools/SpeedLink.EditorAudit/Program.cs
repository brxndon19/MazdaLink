using System.Text.Json;
using SpeedLink.Core;

// Local validation only. The caller supplies its own ROMs and independent golden values.
if (args.Length == 2 && args[0] == "--expect-rejected")
{
    var image = File.ReadAllBytes(args[1]); var inspection = new RomInspector().Inspect(image);
    try { _ = new RomEditor(image, args[1]); }
    catch (InvalidDataException error)
    {
        if (!error.Message.Contains("program area", StringComparison.Ordinal)) throw;
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, rejectedModifiedProgram = true, existingIdentityBootAndChecksumLayoutAccepted = inspection.CanCorrectChecksum, reason = error.Message })); return;
    }
    throw new Exception("Expected the modified program to be rejected.");
}
if (args.Length != 2) throw new ArgumentException("Usage: SpeedLink.EditorAudit <golden.json> <new-output-folder>");
var golden = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
string outputRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(outputRoot);
var reports = new List<object>();
foreach (var item in golden.EnumerateArray())
{
    string path = item.GetProperty("path").GetString()!; byte[] original = File.ReadAllBytes(path);
    var editor = new RomEditor(original, path); int cells = 0, bits = 0, numeric = 0, choices = 0, nonfinite = 0;
    string folder = Path.Combine(outputRoot, editor.Definition.Calibration); Directory.CreateDirectory(folder);
    foreach (var map in item.GetProperty("maps").EnumerateArray())
    {
        string name = map.GetProperty("name").GetString()!;
        var table = editor.Definition.Tables.Single(t => t.Name == name);
        var expected = map.GetProperty("values").EnumerateArray().ToArray();
        if (expected.Length != table.Count) throw new Exception("Dimension mismatch: " + name);
        var unchanged = new List<CellEdit>();
        for (int i = 0; i < expected.Length; i++)
        {
            double actual = editor.Read(table, i); cells++;
            if (expected[i].ValueKind == JsonValueKind.Null) { if (double.IsFinite(actual)) throw new Exception("Nonfinite mismatch: " + name); nonfinite++; continue; }
            double value = expected[i].GetDouble();
            if (!double.IsFinite(actual) || Math.Abs(actual - value) > Math.Max(1e-12, Math.Abs(value) * 1e-12)) throw new Exception($"Independent decode mismatch: {name} [{i}]: {value:R} vs {actual:R}");
            unchanged.Add(new(table, i, actual));
        }
        editor.Apply(unchanged, "No-op roundtrip");
        if (editor.CanUndo || !original.AsSpan().SequenceEqual(editor.Snapshot())) throw new Exception("No-op changed bytes: " + name);
        if (table.Bits.Length > 0)
        {
            editor.Apply(Enumerable.Range(0, table.Count).Select(i => new CellEdit(table, i, 1 - editor.Read(table, i))), "All bit controls"); bits += table.Count;
        }
        else if (table.Kind == "Switch") { editor.Apply([new(table, 0, table.States.First(s => s.Value != editor.Read(table, 0)).Value)], "Named state"); choices++; }
        else
        {
            double candidate = table.Decode(100);
            if (candidate == editor.Read(table, table.Count - 1)) candidate = table.Decode(120);
            editor.Apply([new(table, table.Count - 1, candidate)], "Numeric encoding"); numeric++;
        }
        var modified = editor.Snapshot();
        if (modified.AsSpan().SequenceEqual(original)) throw new Exception("Audit did not change this control: " + name);
        _ = new RomEditor(modified, path);
        for (int i = 0; i < original.Length; i++) if (original[i] != modified[i] && (i < table.Address || i >= table.End)) throw new Exception("Edit escaped table storage: " + name);
        if (editor.CanUndo) { editor.Undo(); if (!editor.Snapshot().AsSpan().SequenceEqual(original)) throw new Exception("Undo mismatch: " + name); }
    }
    string noEdit = Path.Combine(folder, "NO_EDIT_ROUNDTRIP.bin"); editor.SaveNew(noEdit);
    if (!File.ReadAllBytes(noEdit).AsSpan().SequenceEqual(original)) throw new Exception("Roundtrip export mismatch.");
    var inspector = new RomInspector();
    var profile = inspector.ProfileFor(original)!;
    var sumRanges = profile.SumRanges ?? [new(profile.ChecksumStart, original.Length - profile.ChecksumStart)];
    // Gen 2 has no bitfield tables. Exercise an editable float covered by its native sum.
    var changedTable = editor.Definition.Tables.FirstOrDefault(t => t.Bits.Length > 0) ??
        editor.Definition.Tables.First(t => !t.IsChoice && t.Storage == "float" &&
            sumRanges.Any(r => t.Address >= r.Address && t.Address + t.Width <= r.Address + r.Length));
    double beforeEdit = editor.Read(changedTable, 0);
    double editValue = changedTable.Bits.Length > 0 ? 1 - beforeEdit :
        changedTable.Decode(beforeEdit == changedTable.Decode(100) ? 120 : 100);
    editor.Apply([new(changedTable, 0, editValue)], "Audit export edit"); var edited = editor.Snapshot();
    string saved = Path.Combine(folder, "EDITOR_TEST_ONLY_NOT_FOR_FLASHING.bin"); editor.SaveNew(saved);
    var exported = File.ReadAllBytes(saved); var check = inspector.Inspect(exported);
    if (check.ChecksumValid != true) throw new Exception("Edited export checksum invalid.");
    for (int i = 0; i < edited.Length; i++) if (edited[i] != exported[i] && (i < check.CorrectionAddress || i >= check.CorrectionAddress + 4)) throw new Exception("Save changed unrelated bytes.");
    if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original)) throw new Exception("Original changed.");
    var faultyChecksum = (byte[])original.Clone(); faultyChecksum[check.CorrectionAddress!.Value] ^= 1;
    var repair = new RomEditor(faultyChecksum, path); string repairedPath = Path.Combine(folder, "CHECKSUM_FAULT_REPAIR_TEST_ONLY.bin"); repair.SaveNew(repairedPath);
    if (!inspector.Inspect(File.ReadAllBytes(repairedPath)).ProfileValid) throw new Exception("Injected checksum fault was not repaired.");
    repair.Undo(); if (!repair.Snapshot().AsSpan().SequenceEqual(faultyChecksum)) throw new Exception("Checksum correction undo mismatch.");
    reports.Add(new { calibration = editor.Definition.Calibration, controls = editor.Definition.Tables.Count, cellsCompared = cells, nonfinite, bitSwitchesEdited = bits, numericControlsEdited = numeric, namedSwitchesEdited = choices, originalSha256 = RomInspector.Hash(original), exportedSha256 = RomInspector.Hash(exported), independentGoldenMatched = true, allNoOpEditsByteIdentical = true, allTableEditsConfinedToStorage = true, undoRestoredBytes = true, noEditExportIdentical = true, checksumExportVerified = true, originalUnchanged = true });
}
File.WriteAllText(Path.Combine(outputRoot, "Audit.json"), JsonSerializer.Serialize(new { passed = true, profiles = reports.Count, results = reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed independent editor audit for {reports.Count} ROM profiles. Evidence: {outputRoot}");
