// Adapted from SpeedLink (GPL-3.0-or-later); see ATTRIBUTIONS.md and LICENSE.
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MazdaLink.J2534;

public enum J2534AdapterKind { MongoosePlusIsoCan3 }

public sealed record J2534Driver(string Name, string LibraryPath, J2534AdapterKind Kind = J2534AdapterKind.MongoosePlusIsoCan3)
{
    public bool Experimental => Kind == J2534AdapterKind.MongoosePlusIsoCan3;
    public string AdapterName => Name + (Experimental ? " (experimental)" : "");
    public override string ToString() => Name;
}

public static class J2534Drivers
{
    /// <summary>Registered 32-bit 04.04 entries for the selected adapter; no arbitrary DLL browsing.</summary>
    public static IReadOnlyList<J2534Driver> Discover(J2534AdapterKind kind = J2534AdapterKind.MongoosePlusIsoCan3)
    {
        if (!OperatingSystem.IsWindows())
            return [];
        return DiscoverWindows(kind);
    }

    public static bool AcceptsRegistration(J2534AdapterKind kind, string name, string vendor, bool iso15765)
    {
        if (kind != J2534AdapterKind.MongoosePlusIsoCan3 || !iso15765)
            return false;
        string normalized = string.Concat(name.Replace("+", "Plus", StringComparison.Ordinal).Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return normalized.Contains("MONGOOSEPLUS", StringComparison.Ordinal) &&
            (normalized.Contains("ISOCAN3", StringComparison.Ordinal) || normalized.Contains("ISO3", StringComparison.Ordinal));
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<J2534Driver> DiscoverWindows(J2534AdapterKind kind)
    {
        var result = new List<J2534Driver>();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var support = machine.OpenSubKey(@"SOFTWARE\PassThruSupport.04.04");
        if (support is null)
            return result;
        foreach (var keyName in support.GetSubKeyNames())
        {
            using var key = support.OpenSubKey(keyName);
            if (key is null)
                continue;
            var name = key.GetValue("Name") as string ?? keyName;
            var vendor = key.GetValue("Vendor") as string ?? "";
            var path = key.GetValue("FunctionLibrary") as string;
            if (string.IsNullOrWhiteSpace(path) ||
                !AcceptsRegistration(kind, name, vendor, key.GetValue("ISO15765") is int capability && capability > 0))
                continue;
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            // The 32-bit registry can reference System32; an x64 inspection process
            // needs the actual WOW64 path. A 32-bit reader is redirected by Windows.
            if (Environment.Is64BitProcess)
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System) + Path.DirectorySeparatorChar;
                if (path.StartsWith(system, StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), path[system.Length..]);
            }
            if (File.Exists(path) && !result.Any(item => item.LibraryPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(name, path, kind));
        }
        return result;
    }
}
