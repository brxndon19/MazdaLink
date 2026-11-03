using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class DefinitionImportTests
{
    public static IEnumerable<object[]> Profiles => RomInspector.KnownProfiles.Select(p => new object[] { p.Calibration });
    private static string WriteEditor(TempFolder folder, string calibration, Action<XDocument>? change = null)
    {
        var xml = XDocument.Parse(Encoding.UTF8.GetString(DefinitionLibrary.BundledBytes(DefinitionKind.Editor, calibration)));
        xml.Root!.Element("rom")!.Element("romid")!.Element("version")!.Value = "import-test";
        change?.Invoke(xml); string path = Path.Combine(folder.Path, Guid.NewGuid() + ".xml"); xml.Save(path); return path;
    }
    private static string WriteLogger(TempFolder folder, string calibration, Action<JsonNode>? change = null)
    {
        var json = JsonNode.Parse(DefinitionLibrary.BundledBytes(DefinitionKind.Logger, calibration))!;
        json["definitionVersion"] = "import-test"; change?.Invoke(json);
        string path = Path.Combine(folder.Path, Guid.NewGuid() + ".json"); File.WriteAllText(path, json.ToJsonString()); return path;
    }
    [Theory, MemberData(nameof(Profiles))]
    public void EditorImportPersistsEditsExportsAndRevertsWithoutChangingOpenDocument(string calibration)
    {
        using var folder = new TempFolder(); string installed = Path.Combine(folder.Path, "installed"); var library = new DefinitionLibrary(installed);
        var original = library.Load(); var profile = Fixture.Profiles.Single(p => p.Calibration == calibration); byte[] input = Fixture.Image(profile);
        var open = new RomEditor(input, Path.Combine(folder.Path, "source.bin"), Fixture.Inspector(), Fixture.TuningGuard, original.Editors);
        var candidate = library.Preview(WriteEditor(folder, calibration)); library.Install(candidate, null);
        var loaded = new DefinitionLibrary(installed).Load(); Assert.Equal(20, loaded.Entries.Count);
        Assert.Equal("import-test", loaded.Editors.Single(d => d.Calibration == calibration).Version); Assert.NotEqual("import-test", open.Definition.Version);
        var editor = new RomEditor(input, Path.Combine(folder.Path, "source.bin"), Fixture.Inspector(), Fixture.TuningGuard, loaded.Editors);
        var table = editor.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        editor.Apply([new(table, 0, 1234)], "Imported map edit"); string output = Path.Combine(folder.Path, "edited.bin"); editor.SaveNew(output);
        Assert.True(Fixture.Inspector().Inspect(File.ReadAllBytes(output)).ChecksumValid);
        var reopened = new RomEditor(File.ReadAllBytes(output), output, Fixture.Inspector(), Fixture.TuningGuard, loaded.Editors);
        Assert.Equal(1234, reopened.Read(reopened.Definition.Tables.Single(t => t.Name == table.Name), 0));
        library.UseBundled(loaded.Entries.Single(e => e.Kind == DefinitionKind.Editor && e.Calibration == calibration));
        Assert.False(library.Load().Entries.Single(e => e.Kind == DefinitionKind.Editor && e.Calibration == calibration).Imported);
        Assert.Equal("import-test", editor.Definition.Version);
    }
    [Theory, MemberData(nameof(Profiles))]
    public void LoggerImportOverridesDecoderAndPreservesBundledFallback(string calibration)
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        string path = WriteLogger(folder, calibration, json =>
        {
            var rpm = json["channels"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == "engine_rpm")!;
            rpm["name"] = "Updated engine speed"; rpm["decode"]!["multiplier"] = 2;
        });
        var candidate = library.Preview(path); library.Install(candidate, null); var loaded = library.Load();
        var logger = loaded.LoggerFor(calibration)!; Assert.Equal("import-test", logger.DefinitionVersion);
        Assert.Equal("Updated engine speed", logger.Channels.Single(c => c.Id == "engine_rpm").Name);
        string query = logger.Channels.Single(c => c.Id == "engine_rpm").Query!;
        byte[] response = Convert.FromHexString("62" + query + "0001"); Assert.Equal(2, logger.Decode("engine_rpm", response));
        Assert.NotEqual("import-test", LoggerDefinition.ForCalibration(calibration)!.DefinitionVersion);
        library.UseBundled(loaded.Entries.Single(e => e.Kind == DefinitionKind.Logger && e.Calibration == calibration));
        Assert.NotEqual("import-test", library.Load().LoggerFor(calibration)!.DefinitionVersion);
    }
    [Fact]
    public void StaleReplacementPreviewMutationAndConcurrentStoreAreRejected()
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        var candidate = library.Preview(WriteEditor(folder, "L38KEH000")); library.Install(candidate, null);
        Assert.Throws<IOException>(() => library.Install(candidate, null));
        var row = library.Load().Entries.Single(e => e.Kind == DefinitionKind.Editor && e.Calibration == "L38KEH000");
        using (var ownership = new FileStream(Path.Combine(library.Folder, ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => library.UseBundled(row));
        var next = library.Preview(WriteEditor(folder, "L38KEH000", xml => xml.Descendants("version").Single().Value = "next"));
        library.Install(next, row.InstalledSha256);
        Assert.Equal(candidate.Content, File.ReadAllBytes(Path.Combine(library.Folder, "L38KEH000.xml.previous")));
        Assert.Throws<IOException>(() => library.UseBundled(row));
        candidate.Content[^1] ^= 1; Assert.Throws<InvalidDataException>(() => library.Install(candidate, next.Entry.Sha256));
        Assert.Equal("next", library.Load().Editors.Single(d => d.Calibration == "L38KEH000").Version);
        Assert.Empty(Directory.EnumerateFiles(library.Folder, "*.partial"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidInstalledFileFallsBackAndCanBeRemoved(bool oversized)
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed")); Directory.CreateDirectory(library.Folder);
        string path = Path.Combine(library.Folder, "L38KEH000.xml");
        File.WriteAllBytes(path, oversized ? new byte[DefinitionLibrary.MaximumXmlBytes + 1] : "invalid XML"u8.ToArray());
        var row = library.Load().Entries.Single(e => e.Kind == DefinitionKind.Editor && e.Calibration == "L38KEH000");
        Assert.False(row.Imported); Assert.NotNull(row.Problem); Assert.NotNull(row.InstalledSha256);
        library.UseBundled(row); Assert.False(File.Exists(path)); Assert.Null(library.Load().Entries.Single(e => e.Kind == row.Kind && e.Calibration == row.Calibration).Problem);
    }
    [Theory]
    [InlineData("identity")] [InlineData("code")] [InlineData("checksum")] [InlineData("marker")] [InlineData("dimensions")] [InlineData("dynamic")] [InlineData("empty")]
    public void InvalidEditorDefinitionsNeverInstall(string fault)
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        string path = WriteEditor(folder, "L38KEH000", xml =>
        {
            var rom = xml.Root!.Element("rom")!; var table = rom.Elements("table").First();
            if (fault == "identity") rom.Element("romid")!.Element("internalidstring")!.Value = "UNKNOWN00L3T6021";
            if (fault is "code" or "checksum" or "marker") table.SetAttributeValue("storageaddress", fault == "code" ? "0x48004" : fault == "checksum" ? "0x48000" : "0x8000");
            if (fault == "dimensions") table.SetAttributeValue("sizex", int.MaxValue);
            if (fault == "dynamic") table.Add(new XElement("table", new XAttribute("type", "X Axis")));
            if (fault == "empty") rom.Elements("table").Remove();
        });
        Assert.Throws<InvalidDataException>(() => library.Preview(path)); Assert.False(Directory.Exists(library.Folder));
    }
    [Fact]
    public void ReducedMapListUsesCanonicalProgramMaskAndStillRejectsProgramEdits()
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        string path = WriteEditor(folder, "L38KEH000", xml => xml.Root!.Element("rom")!.Elements("table").Skip(1).Remove());
        library.Install(library.Preview(path), null); var snapshot = library.Load(); var profile = Fixture.Profiles.Single(p => p.Calibration == "L38KEH000");
        var input = Fixture.Image(profile); var editor = new RomEditor(input, "source.bin", Fixture.Inspector(), Fixture.TuningGuard, snapshot.Editors);
        Assert.Single(editor.Definition.Tables);
        input[0x48100] ^= 1;
        Assert.Throws<InvalidDataException>(() => new RomEditor(input, "modified.bin", Fixture.Inspector(), Fixture.TuningGuard, snapshot.Editors));
    }
    [Theory]
    [InlineData("service")] [InlineData("fingerprint")] [InlineData("query")] [InlineData("binding")] [InlineData("cycle")]
    public void InvalidLoggerDefinitionsNeverInstall(string fault)
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        string path = WriteLogger(folder, "L38KEH000", json =>
        {
            if (fault == "service") json["protocol"]!["service"] = "0x2E";
            if (fault == "fingerprint") json["codeFingerprints"]![0]!["sha256"] = new string('0', 64);
            if (fault == "query") json["queries"]!["FFFF"] = new JsonObject { ["requestHex"] = "22FFFF", ["positiveHeaderHex"] = "62FFFF", ["dataBytes"] = 2 };
            if (fault == "binding") json["calibration"]!["bindings"]!["map_gain_kpa_per_v"]!["sources"]![0]!["offset"] = "0xFFFFFF";
            if (fault == "cycle")
            {
                var channel = json["channels"]!.AsArray().First(c => c!["kind"]!.GetValue<string>() == "calculated")!;
                channel["calculation"]!["inputs"]![0] = channel["id"]!.GetValue<string>();
            }
        });
        Assert.Throws<InvalidDataException>(() => library.Preview(path)); Assert.False(Directory.Exists(library.Folder));
    }
    [Fact]
    public void XmlEntitiesOversizeAndMismatchedInstalledIdentityAreRejected()
    {
        using var folder = new TempFolder(); var library = new DefinitionLibrary(Path.Combine(folder.Path, "installed"));
        string path = Path.Combine(folder.Path, "bad.xml"); File.WriteAllText(path, "<!DOCTYPE roms [<!ENTITY x SYSTEM 'file:///private'>]><roms>&x;</roms>");
        Assert.Throws<InvalidDataException>(() => library.Preview(path));
        File.WriteAllBytes(path, new byte[DefinitionLibrary.MaximumXmlBytes + 1]); Assert.Throws<InvalidDataException>(() => library.Preview(path));
        Directory.CreateDirectory(library.Folder); File.WriteAllBytes(Path.Combine(library.Folder, "L38KEH000.xml"), DefinitionLibrary.BundledBytes(DefinitionKind.Editor, "L33XEH000"));
        var row = library.Load().Entries.Single(e => e.Kind == DefinitionKind.Editor && e.Calibration == "L38KEH000"); Assert.False(row.Imported); Assert.NotNull(row.Problem);
    }
}
