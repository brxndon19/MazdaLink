using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpeedLink.Core;

public sealed record WordGuard(int Address, uint Value);
public sealed record RomRange(int Address, int Length);
public sealed record MarkerGuard(int Address, ushort Value);
public sealed record RomProfile(string Calibration, string Model, int IdentityOffset, string Identity,
    string BootSha256, int ChecksumStart, int CorrectionAddress, WordGuard[] LayoutWords,
    int? LoopAddress, int? LoopSize, string? LoopSha256)
{
    public string Family { get; init; } = "Gen1";
    public int BootLength { get; init; } = 0x8000;
    public RomRange[]? BootDataRanges { get; init; }
    public RomRange[]? SumRanges { get; init; }
    public RomRange[]? RoutineRanges { get; init; }
    public MarkerGuard[]? Markers { get; init; }
}

public sealed record RomInspection(int Size, string? Calibration, string? Model, string Sha256,
    string BootSha256, bool ProfileRecognized, bool IdentityValid, bool BootRecognized,
    bool MarkersValid, bool ChecksumLayoutRecognized, string? Checksum, string? ExpectedChecksum,
    bool? ChecksumValid, int? ChecksumStart, int? CorrectionAddress, bool CanCorrectChecksum,
    bool ProfileValid, string IntegrityPolicy = "additive_correction")
{
    public bool CanExport => CanCorrectChecksum || IntegrityPolicy == "native_code_validation" && ProfileValid;
    public object ToManifest() => new Dictionary<string, object?>
    {
        ["size"] = Size,
        ["calibration"] = Calibration,
        ["model"] = Model,
        ["sha256"] = Sha256,
        ["bootSHA256"] = BootSha256,
        ["profileRecognized"] = ProfileRecognized,
        ["identityValid"] = IdentityValid,
        ["bootRecognized"] = BootRecognized,
        ["markersValid"] = MarkersValid,
        ["checksumLayoutRecognized"] = ChecksumLayoutRecognized,
        ["checksum"] = Checksum,
        ["expectedChecksum"] = ExpectedChecksum,
        ["checksumValid"] = ChecksumValid,
        ["checksumStart"] = ChecksumStart,
        ["correctionAddress"] = CorrectionAddress,
        ["canCorrectChecksum"] = CanCorrectChecksum,
        ["canExport"] = CanExport,
        ["integrityPolicy"] = IntegrityPolicy,
        ["profileValid"] = ProfileValid,
        ["vehicleMatchVerified"] = false
    };
}

public sealed class RomInspector
{
    public const int RomSize = 0x100000;
    public const int BootSize = 0x8000;
    public const uint ChecksumTarget = 0x5AA55AA5;
    public static IReadOnlyList<RomProfile> KnownProfiles { get; } = LoadProfiles();
    private readonly IReadOnlyList<RomProfile> profiles;
    private readonly TuningCodeGuard codeGuard;
    public RomInspector(IReadOnlyList<RomProfile>? profiles = null, TuningCodeGuard? codeGuard = null)
    {
        this.profiles = profiles ?? KnownProfiles;
        this.codeGuard = codeGuard ?? new();
    }
    private static IReadOnlyList<RomProfile> LoadProfiles()
    {
        using var stream = typeof(RomInspector).Assembly.GetManifestResourceStream("SpeedLink.Core.Profiles.json")
            ?? throw new InvalidOperationException("Known-ROM metadata is missing.");
        return Array.AsReadOnly(JsonSerializer.Deserialize<RomProfile[]>(stream) ?? throw new InvalidDataException("Invalid ROM metadata."));
    }
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static string BootHash(ReadOnlySpan<byte> data, RomProfile profile)
    {
        if (data.Length != RomSize || profile.BootLength is < 1 or > RomSize)
            throw new InvalidDataException("Complete ROM and valid boot fingerprint bounds required.");
        var boot = data[..profile.BootLength];
        if (profile.BootDataRanges is not { Length: > 0 }) return Hash(boot);
        // Normalize established per-ECU records in a temporary fingerprint copy only.
        var normalized = boot.ToArray(); int end = 0;
        foreach (var range in profile.BootDataRanges.OrderBy(r => r.Address))
        {
            if (range.Address < end || range.Length < 1 || range.Address > normalized.Length - range.Length)
                throw new InvalidDataException("Invalid per-ECU boot data range.");
            normalized.AsSpan(range.Address, range.Length).Clear();
            end = range.Address + range.Length;
        }
        return Hash(normalized);
    }
    public static bool MatchesCalibrationId(RomProfile profile, string? calibration) => calibration is not null &&
        new[] { profile.Calibration, profile.Identity }.Any(id => System.Text.RegularExpressions.Regex.IsMatch(calibration,
            "(?<![A-Z0-9])" + System.Text.RegularExpressions.Regex.Escape(id) + "(?![A-Z0-9])",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant));
    public RomProfile? ProfileFor(ReadOnlySpan<byte> data)
    {
        RomProfile? found = null;
        foreach (var profile in profiles)
            if (data.Length >= profile.IdentityOffset + 9 && data.Slice(profile.IdentityOffset, 9).SequenceEqual(Encoding.ASCII.GetBytes(profile.Calibration)))
            {
                if (found is not null)
                    return null;
                found = profile;
            }
        return found;
    }
    public static uint Sum(ReadOnlySpan<byte> data, RomProfile profile)
    {
        if (data.Length != RomSize)
            throw new InvalidDataException("A complete 1 MiB ROM is required.");
        uint sum = 0;
        foreach (var range in profile.SumRanges ?? [new(profile.ChecksumStart, data.Length - profile.ChecksumStart)])
            for (var at = range.Address; at < range.Address + range.Length; at += 4)
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4)));
        return sum;
    }
    public RomInspection Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length != RomSize)
            throw new InvalidDataException($"Expected {RomSize:N0} bytes; this file has {data.Length:N0}.");
        var profile = ProfileFor(data);
        var boot = profile is null ? Hash(data[..BootSize]) : BootHash(data, profile);
        if (profile is null)
            return new(data.Length, null, null, Hash(data), boot, false, false, false, false, false, null, null, null, null, null, false, false, "unavailable");
        var markers = data[0x8002] == 0x5A && data[0x8003] == 0xA5 && data[^2] == 0xA5 && data[^1] == 0x5A;
        if (profile.Markers is not null)
        {
            markers = true;
            foreach (var guard in profile.Markers)
                markers &= BinaryPrimitives.ReadUInt16BigEndian(data.Slice(guard.Address, 2)) == guard.Value;
        }
        var identity = data.Slice(profile.IdentityOffset, profile.Identity.Length).SequenceEqual(Encoding.ASCII.GetBytes(profile.Identity));
        var bootKnown = boot == profile.BootSha256;
        var layout = identity;
        foreach (var guard in profile.LayoutWords)
            layout &= BinaryPrimitives.ReadUInt32BigEndian(data.Slice(guard.Address, 4)) == guard.Value;
        if (profile.LoopAddress is int at && profile.LoopSize is int size)
            layout &= Hash(data.Slice(at, size)) == profile.LoopSha256;
        if (profile.RoutineRanges is not null)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var range in profile.RoutineRanges) hash.AppendData(data.Slice(range.Address, range.Length));
            layout &= Convert.ToHexStringLower(hash.GetHashAndReset()) == profile.LoopSha256;
        }
        if (profile.Calibration == "L38KEH000")
            layout &= bootKnown;
        uint? sum = layout ? Sum(data, profile) : null;
        bool? valid = sum.HasValue ? sum.Value == ChecksumTarget : null;
        var correct = identity && bootKnown && markers && layout;
        bool canCorrect = correct && profile.CorrectionAddress >= 0;
        if (canCorrect && profile.Family == "Gen2")
            canCorrect = TuningDefinition.Known.FirstOrDefault(d => d.Calibration == profile.Calibration) is { } definition &&
                codeGuard.Matches(data, definition, profile);
        return new(data.Length, profile.Calibration, profile.Model, Hash(data), boot, true, identity, bootKnown,
            markers, layout, sum?.ToString("X8"), layout ? ChecksumTarget.ToString("X8") : null, valid,
            profile.ChecksumStart, profile.CorrectionAddress >= 0 ? profile.CorrectionAddress : null,
            canCorrect, correct && valid == true,
            profile.CorrectionAddress < 0 ? "native_code_validation" : profile.Family == "Gen2" ? "native_code_correction" : "additive_correction");
    }
    public byte[] CorrectedCopy(ReadOnlySpan<byte> data)
    {
        var info = Inspect(data);
        if (info.IntegrityPolicy == "native_code_validation")
        {
            if (!info.CanExport) throw new InvalidDataException("Native code checksum, markers or layout are invalid. Automatic code repair is unavailable.");
            return data.ToArray();
        }
        if (!info.CanCorrectChecksum)
            throw new InvalidDataException("Checksum correction requires a recognized identity, boot and supported program layout.");
        var profile = ProfileFor(data)!;
        var output = data.ToArray();
        var old = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(profile.CorrectionAddress, 4));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(profile.CorrectionAddress, 4), unchecked(old + ChecksumTarget - Sum(data, profile)));
        if (!Inspect(output).ProfileValid)
            throw new InvalidDataException("Checksum-corrected copy failed verification.");
        return output;
    }
}
