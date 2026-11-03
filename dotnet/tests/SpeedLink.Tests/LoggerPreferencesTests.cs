using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class LoggerPreferencesTests
{
    [Fact]
    public void Migration_preserves_ids_absent_from_the_startup_profile()
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, """{"version":1,"target":10,"stoich":"14.1","record":false,"channels":[{"id":"battery_voltage","selected":true,"unit":"V","plotted":true},{"id":"engine_rpm","selected":false,"unit":"RPM"}]}""");
        var prefs = LoggerPreferences.Load(path);
        prefs.Profiles["gen1"] = [new("engine_rpm", true, "rpm", false)]; prefs.Save(path);
        var loaded = LoggerPreferences.Load(path);
        Assert.Equal(3, loaded.Version); Assert.Equal(0, loaded.Target); Assert.False(loaded.Record);
        Assert.True(loaded.ForProfile("gen2").Single(c => c.Id == "battery_voltage").Plotted);
        Assert.False(loaded.ForProfile("gen2").Single(c => c.Id == "engine_rpm").Selected);
        Assert.True(loaded.ForProfile("gen1").Single().Selected);
    }

    [Fact]
    public void Profiles_and_global_choices_roundtrip_without_rom_paths_or_bytes()
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "settings.json");
        var prefs = new LoggerPreferences { Target = 20, Stoich = "14.0", ActiveCalibration = "L3CGEE000" };
        prefs.Profiles["one"] = [new("engine_rpm", false, "rpm", false)];
        prefs.Profiles["two"] = [new("engine_rpm", true, "RPM", true), new("battery_voltage", true, "V", true)]; prefs.Save(path);
        var loaded = LoggerPreferences.Load(path);
        Assert.Equal(20, loaded.Target); Assert.Equal("14.0", loaded.Stoich); Assert.Equal("L3CGEE000", loaded.ActiveCalibration);
        Assert.False(loaded.ForProfile("one").Single().Selected); Assert.Equal(2, loaded.ForProfile("two").Count);
        Assert.Empty(Directory.GetFiles(folder.Path, "*.pending-*"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Old_default_scan_caps_migrate_to_maximum_without_losing_choices(int version)
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, "{\"version\":" + version + ",\"target\":5,\"stoich\":\"14.0\",\"record\":false,\"channels\":[{\"id\":\"engine_rpm\",\"selected\":true,\"unit\":\"rpm\",\"plotted\":true}]}");
        var prefs = LoggerPreferences.Load(path);
        Assert.Equal(0, prefs.Target); Assert.Equal(3, prefs.Version); Assert.False(prefs.Record); Assert.Equal("14.0", prefs.Stoich);
        Assert.True(prefs.ForProfile("test").Single().Selected); Assert.True(prefs.ForProfile("test").Single().Plotted);
        prefs.Save(path); Assert.Equal(0, LoggerPreferences.Load(path).Target);
    }

    [Theory]
    [InlineData("{\"version\":99}")]
    [InlineData("{\"version\":2,\"channels\":null}")]
    [InlineData("{\"version\":2,\"profiles\":null}")]
    [InlineData("{\"version\":2,\"channels\":[{\"id\":\"a\",\"unit\":\"V\"},{\"id\":\"a\",\"unit\":\"V\"}]}")]
    public void Unsupported_or_invalid_settings_are_rejected(string json)
    {
        using var folder = new TempFolder(); string path = Path.Combine(folder.Path, "settings.json"); File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => LoggerPreferences.Load(path));
    }
}
