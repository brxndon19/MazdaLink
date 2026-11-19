namespace MazdaLink.Core;

// Separate capability: the read-only RAM self-test interface cannot select this program.
public interface IProtectedInitializationChannel : IBenchMonitorChannel
{
    void SendInitializationStep(int index);
    RamSelfTestTransportDiagnostics? InitializationDiagnostics => null;
}

public static class Zyj2ProtectedInitializationPolicy
{
    public const string ImageSha256 = "85ccb46064aa483d64f53b5666017b57b2624ea015fcae0299ef5f336587925b";
    private static readonly byte[] image = Convert.FromHexString(
        "080209226083CBF0400EEC00DECBDACCD3CC430B0009CA01ED0120088901A16A000960A0ED0220088901A164000984A5ED0220088901A15E000984A4ED0220088901A1580009D1C0D2C0E4106010632030308902ED0CA14E00097101720144108BF4D1BB6011ED0320088901A1430009D1B86011D2B9D3BA2039202AED0420088901A138000971026011C9FFCA01ED0420088901A12F0009D1AF60112039202AED0420088901A126000971026011C9FFCA01ED0420088901A11D0009D1A96010C901ED0520088901A1150009D1A66010C901ED0520088901A10D0009D1A36010C901ED0520088901A105000984A1ED0620088901A0FF000984A2ED0620088901A0F9000984A6ED0620088901A0F30009D397430B000960A0ED0220088901A0EA000984A5ED0220088901A0E4000984A4ED0220088901A0DE0009EC01E00180A1E00080A2E00280A6DB8AE0FF2B00E000402EE0A580A4E0012A0000090009000900090009000900090009E00080A460B080E3ED0720088901A0BD000984A6CA02ED0720088901A0B6000960A0ED0720088901A0B0000984A5ED0720088901A0AA000984A4ED0720088901A0A40009E47D440844084408E500D36F430B000980E5ED0820088901A096000960A0ED0820088901A090000984A5ED0820088901A08A000984A4ED0820088901A0840009D360430B000960A0ED0920088901A07B000984A5ED0920088901A075000984A4ED0920088901A06F0009E00080A1E00180A2E00480A6DB55E0FF2B00E000402EE0A580A4E0012A0000090009000900090009000900090009E00080A460B080E4ED0920088901A04F000984A6CA04ED0920088901A048000960A0ED0920088901A042000984A5ED0920088901A03C000984A4ED0920088901A0360009E47D440844084408E500D33A430B000980E5ED0A20088901A028000960A0ED0A20088901A022000984A5ED0A20088901A01C000984A4ED0A20088901A0160009D329430B000960A0ED0A20088901A00D000984A5ED0A20088901A007000984A4ED0A20088901A0010009ED002CC88912E00080A480A180A280A662A084A1220B84A2220B84A4220B84A5220B84A6220B22288900ED0B492E480E60D380E266E3E4C0E506D315430B0009D414D315432B0009FFFF8390FFFFE80000000EE4000C0091FFFF8398FFFFEC26FFFFD004FFFFD804FFFFE320FFFFF733FFFFF743FFFFF740FFFFF78400000484FFFF1000FFFF1020FFFF2000FFFF2020000004C200000924000000404D49FFFFFFFF00005A594A3245413030305A595635303530");
    private static readonly IReadOnlyList<RamSelfTestStep> steps = Build().AsReadOnly();
    public static byte[] Image => image.ToArray();
    public static IReadOnlyList<RamSelfTestStep> Steps => steps;
    private static List<RamSelfTestStep> Build()
    {
        if (image.Length != 936 || RomStorage.Hash(image) != ImageSha256)
            throw new InvalidDataException("Protected initialization image integrity failed.");
        return FixedRamScript.Build(image, [0x7A, 0xC6, 0x4D, 0x49]);
    }
}

public sealed record ProtectedInitializationResult(int UploadedBytes, int VerifiedBytes, byte Status,
    byte ProgrammingDownloadResult, byte ErasingDownloadResult, byte LastInitializationResultLowByte)
{
    // Status zero is issued only after full-width initialization return checks and cleanup checks.
    // The low byte is diagnostic information; a nonzero full return can never pass via truncation.
    public bool ProtectionChecksPassed => Status == 0 && ProgrammingDownloadResult == 0 &&
        ErasingDownloadResult == 0 && LastInitializationResultLowByte == 0;
    public string StatusDescription => Status switch
    {
        0 => "Both MCU algorithms downloaded and initialized with protection checks passed",
        1 => "Resident hardware class is not SH7058S",
        2 => "Initial controller protection, mapping or key check failed",
        3 => "RAM emulation is active",
        4 => "Live CAN timing does not establish the fixed 80 MHz CPU parameter",
        5 => "PD8 is not GPIO/input/low",
        6 => "Controller selection registers were not in their initial state",
        7 => "Programming algorithm download or protection check failed",
        8 => "Programming algorithm initialization or protection check failed",
        9 => "Erasing algorithm download or protection check failed",
        10 => "Erasing algorithm initialization or protection check failed",
        11 => "Controller cleanup or protection could not be confirmed",
        12 => "Software ID read inside the monitor no longer matches the ZYJ2 spare",
        _ => "Unknown protected initialization status"
    };
}

public sealed class Zyj2ProtectedInitialization
{
    public ProtectedInitializationResult Run(IProtectedInitializationChannel channel, Action<string> record,
        CancellationToken cancel = default, Action<ProtectedInitializationResult>? observeResult = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int uploaded = 0, verified = 0; byte[]? result = null;
        record("protected-initialization-started-no-erase-or-programming");
        for (int index = 0; index < Zyj2ProtectedInitializationPolicy.Steps.Count; index++)
        {
            cancel.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > TimeSpan.FromSeconds(60)) throw new TransportException("Protected initialization script deadline exceeded; no request was retried.");
            var step = Zyj2ProtectedInitializationPolicy.Steps[index];
            channel.SendInitializationStep(index);
            if (step.Operation == RamSelfTestOperation.Upload)
            {
                uploaded += step.Payload[1] & 7;
                if (uploaded == Zyj2ProtectedInitializationPolicy.Image.Length) record("protected-init-image-uploaded-not-executed");
            }
            if (!step.ExpectsReply) continue;
            var frame = Zyj2RamSelfTest.Receive(channel, step, cancel, maximumFrames: 256);
            // Up to 156 unacknowledged upload packets can leave queued adapter echoes.
            // The fixed 256-frame bound accommodates that burst without clearing ECU errors.
            if (step.Operation == RamSelfTestOperation.VerifyByte && ++verified == Zyj2ProtectedInitializationPolicy.Image.Length)
                record("every-protected-init-image-byte-verified");
            if (step.Operation == RamSelfTestOperation.ExecuteFixedTest)
            {
                result = frame.Data.AsSpan(4, 4).ToArray();
                observeResult?.Invoke(new(uploaded, verified, result[0], result[1], result[2], result[3]));
                record("protected-init-result-marker-received");
            }
        }
        if (uploaded != 936 || verified != 936 || result is null)
            throw new TransportException("Protected initialization evidence is incomplete.");
        record("monitor-return-confirmed-after-protected-initialization");
        return new(uploaded, verified, result[0], result[1], result[2], result[3]);
    }
}
