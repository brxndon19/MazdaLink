namespace MazdaLink.Core;

// Isolated recovery preparation. Cannot be acquired from the read-only binding wrapper.
public interface IRecoveryRehearsalChannel : IRecoveryBindingChannel
{
    void SendRehearsalStep(int index);
    RamSelfTestTransportDiagnostics? RehearsalDiagnostics => null;
}

public static class Zyj2RecoveryRehearsalPolicy
{
    public const string ImageSha256 = "3d6d56b24d9e2629418beda03b40cc436d168cd47bf33754c759a7c6a00f1623";
    private static readonly byte[] image = Convert.FromHexString("080209226083CBF0400EEC00DE65DA66D366430B0009CA01ED0120088901A20F000960A0ED0220088901A209000984A5ED0220088901A203000984A4ED0220088901A1FD0009D15AD25AE4046010632030308902ED0CA1F300097101720144108BF4D1556011600DE26430228B02ED0DA1E6000985116211202A600DD24F202AED0D20088901A1DB0009D14D6011ED0320088901A1D40009D14A6011D24BD34C2039202AED0420088901A1C9000971026011C9FFCA01ED0420088901A1C00009D14160112039202AED0420088901A1B7000971026011C9FFCA01ED0420088901A1AE0009D13B6010C901ED0520088901A1A60009D1386010C901ED0520088901A19E0009D1356010C901ED0520088901A196000984A1ED0620088901A190000984A2ED0620088901A18A000984A6ED0620088901A1840009D1296010C97FCA3DED0E20088901A17B0009D1266010600CCA77ED0E20088901A1720009D1226010C980CA80ED0E20088901A169000971026010C9C0ED0E20088901A1610009D11B60106203D11A6010202AC980ED0E20088901A1550009D1172120D1142120EC01A02C0009FFFF856CFFFFE80000000EE400006200FFFF8578000070000000FFFFFFFFEC26FFFFD004FFFFD804FFFFE320FFFFF733FFFFF743FFFFF740FFFFF784FFFFEC10FFFFBFADFFFFF730FFFFF738FFFFF782FFFF8574FFFF84CCD3D0430B0009D1D08412ED1020088901A118000960A0ED0220088901A112000984A5ED0220088901A10C000984A4ED0220088901A1060009E00180A1E00080A2E00280A6DBBBE0FF2B00E000402EE0A580A4E0012A0000090009000900090009000900090009E00080A460B0ED0720088901A0E7000984A6CA02ED0720088901A0E0000960A0ED0720088901A0DA000984A5ED0720088901A0D4000984A4ED0720088901A0CE0009E47D440844084408D5A4D3A1430B000980E3ED0820088901A0C0000960A0ED0820088901A0BA000984A5ED0820088901A0B4000984A4ED0820088901A0AE0009D1978412ED1020088901A0A7000960C3CB026C03D391430B0009D1918412ED1020088901A09A000960A0ED0920088901A094000984A5ED0920088901A08E000984A4ED0920088901A0880009E00080A1E00180A2E00480A6DB7EE0FF2B00E000402EE0A580A4E0012A0000090009000900090009000900090009E00080A460B0ED0920088901A069000984A6CA04ED0920088901A062000960A0ED0920088901A05C000984A5ED0920088901A056000984A4ED0920088901A0500009E47D440844084408D565D364430B000980E3ED0A20088901A042000960A0ED0A20088901A03C000984A5ED0A20088901A036000984A4ED0A20088901A0300009D1588412ED1020088901A029000960C3CB046C03D153E0008011E740D45644108BFDB0630009D14F8412ED1020088901A016000947108BF1D14A841180E4600CE20830228902ED0FA00A00098412ED1020088901A004000960C3CB086C03ED002CC88912E00080A480A180A280A662A084A1220B84A2220B84A4220B84A5220B84A6220B22288900ED0B2CC8891ED1356210D1366010202AC98020088B15D1346010202AC98020088B0FD1326010C980CA8020088B0971026010C9C020088B0460C3CB106C03A0010009ED1060C380E5492E480E60D380E266E3E4C0E506D327430B0009D426D327432B0009D11D841220088B25D11C60106303C97FCA3D20088B206033C880891B923E2121D1156010CA802100D21522006303E04040108BFD6020203AC98020088B05D2116020203AC98020088901E0018012841170018011000B0009D107E0018012000B00090009FFFF1000FFFF1020FFFF2000FFFF2020FFFF84CCFFFF8574FFFFEC10FFFFF738FFFFF782FFFFF730000186A0000004C20000092400000040A53D00094D52FFFF00000009000000005A594333");
    public static byte[] Image => image.ToArray();
    public static IReadOnlyList<RamSelfTestStep> Steps { get; } = Build().AsReadOnly();
    private static List<RamSelfTestStep> Build()
    {
        if (image.Length != 1404 || RomStorage.Hash(image) != ImageSha256)
            throw new InvalidDataException("Recovery rehearsal image integrity failed.");
        return FixedRamScript.Build(image, [0x7A,0xC6,0x4D,0x52]);
    }
}

// Immutable concatenation: exact live boot binding must precede the fixed RAM script.
public sealed class RecoveryRehearsalPlan(RecoveryBindingPlan binding) : IReadOnlyList<RamSelfTestStep>
{
    public RecoveryBindingPlan Binding { get; } = binding ?? throw new ArgumentNullException(nameof(binding));
    public int Count => Binding.Count + Zyj2RecoveryRehearsalPolicy.Steps.Count;
    public RamSelfTestStep this[int index] => index < 0 || index >= Count
        ? throw new ArgumentOutOfRangeException(nameof(index))
        : index < Binding.Count ? Binding[index] : Zyj2RecoveryRehearsalPolicy.Steps[index-Binding.Count];
    public IEnumerator<RamSelfTestStep> GetEnumerator()
    { for (int index=0;index<Count;index++) yield return this[index]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record RecoveryRehearsalResult(int UploadedBytes, int VerifiedBytes, byte Status,
    byte LastInitializationResultLowByte, byte RamServiceOverflows, byte ObservationFlags)
{
    public bool Passed => UploadedBytes == 1404 && VerifiedBytes == 1404 && Status == 0 &&
        LastInitializationResultLowByte == 0 && RamServiceOverflows is >= 8 and <= 64 && ObservationFlags == 31;
    public string StatusDescription => Status switch
    {
        0 => "Both MCU algorithms initialized with the RAM callback; direct RAM service and cleanup checks passed",
        1 => "Resident hardware class is not SH7058S",
        2 => "Controller protection or mapping check failed",
        3 => "RAM emulation is active",
        4 => "Live CAN timing does not establish the fixed clock",
        5 => "PD8 is not GPIO/input/low",
        6 => "Controller selection registers were not initially zero",
        7 => "Programming algorithm download check failed",
        8 => "Programming algorithm initialization check failed",
        9 => "Erasing algorithm download check failed",
        10 => "Erasing algorithm initialization check failed",
        11 => "Controller cleanup or protection could not be confirmed",
        12 => "Preserved ZYC3 target marker did not match",
        13 => "Native counter guard is invalid or not below 100",
        14 => "Existing timer/boot marker or PB15 GPIO/output state is unexpected",
        15 => "Too few timer overflows observed in the fixed RAM-only rehearsal",
        16 => "PB15 output/shadow/pin or configuration check failed",
        _ => "Unknown recovery rehearsal status"
    };
}

public sealed class Zyj2RecoveryRehearsal
{
    public RecoveryRehearsalResult Run(IRecoveryRehearsalChannel channel, Action<string> record,
        CancellationToken cancel = default, Action<RecoveryRehearsalResult>? observeResult = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int uploaded=0,verified=0; RecoveryRehearsalResult? result=null;
        record("fixed-recovery-rehearsal-started-initialization-and-ram-service-only");
        for (int index=0;index<Zyj2RecoveryRehearsalPolicy.Steps.Count;index++)
        {
            cancel.ThrowIfCancellationRequested();
            if(elapsed.Elapsed >= TimeSpan.FromSeconds(60)) throw new TransportException("Recovery rehearsal script deadline exceeded. No retry was attempted.");
            var step=Zyj2RecoveryRehearsalPolicy.Steps[index]; channel.SendRehearsalStep(index);
            if(step.Operation==RamSelfTestOperation.Upload) uploaded+=step.Payload[1]&7;
            if(!step.ExpectsReply) continue;
            var frame=Zyj2RamSelfTest.Receive(channel,step,cancel,maximumFrames:256);
            if(step.Operation==RamSelfTestOperation.VerifyByte && ++verified==Zyj2RecoveryRehearsalPolicy.Image.Length)
                record("every-recovery-rehearsal-image-byte-verified-before-execution");
            if(step.Operation==RamSelfTestOperation.ExecuteFixedTest)
            {
                result=new(uploaded,verified,frame.Data[4],frame.Data[5],frame.Data[6],frame.Data[7]);
                observeResult?.Invoke(result); record("recovery-rehearsal-result-received");
                if (!result.Passed)
                    throw new TransportException("Recovery rehearsal did not pass. Remove ECU positive power; no monitor request or retry will be sent.");
            }
        }
        if(result is null || uploaded!=Zyj2RecoveryRehearsalPolicy.Image.Length || verified!=uploaded)
            throw new TransportException("Recovery rehearsal evidence is incomplete.");
        record("monitor-return-confirmed-after-recovery-rehearsal");
        return result;
    }
}
