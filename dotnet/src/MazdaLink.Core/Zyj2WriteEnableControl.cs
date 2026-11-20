namespace MazdaLink.Core;

// Isolated GPIO experiment. Cannot be acquired from the read-only binding wrapper.
public interface IWriteEnableControlChannel : IRecoveryBindingChannel
{
    void SendControlStep(int index);
    RamSelfTestTransportDiagnostics? ControlDiagnostics => null;
}

public static class Zyj2WriteEnableControlPolicy
{
    public const string ImageSha256 = "5d6a904f85c225e12f29d171fcc1c1a88fc1bd27ac55ee5a35083b73909326c9";
    private static readonly byte[] image = Convert.FromHexString("08026083CBF0400EEC00DE9FDA9FD3A0430B0009CA01ED0120088901A0E30009D19CD29DE4106010632030308902ED02A0D900097101720144108BF4D1976011600DE26430228B02ED03A0CC000985116211202A600DD292202AED0320088901A0C1000960A0ED0420088901A0BB000984A1ED0420088901A0B5000984A2ED0420088901A0AF000984A4ED0420088901A0A9000984A5ED0420088901A0A3000984A6ED0420088901A09D0009D17D6011ED0420088901A0960009D17B6011D27CD37C2039202AED0520088901A08B000971026011C9FFCA01ED0520088901A0820009D1726011D272D3722039202AED0520088901A077000971026011C9FFCA01ED0520088901A06E0009DB6B60B1ED0620088901A067000985B1ED0620088901A061000985B2ED0620088901A05B0009D1626010C901ED0620088901A0530009D35F430B000960A0ED0420088901A04A000984A1ED0420088901A044000984A2ED0420088901A03E000984A4ED0420088901A038000984A5ED0420088901A032000984A6ED0420088901A02C0009EC01E03F80B6E0012B00D44A44108BFD60A080E3600CCA80ED0720088901A01B000960B1D245202AED0720088901A013000984B6C901CA01ED0720088901A00B0009D13A6010C901CA01ED0720088901A0020009EC03ED002CC8893EE03E80B684B6C90120088B0260C3CB046C03E0002B0060B120088B0260C3CB086C03D12B6010C90120088B0260C3CB106C0360A080E420088B1B84A120088B1884A220088B1584A420088B1284A520088B0F84A620088B0C85B120088B0985B220088B0660C3C91CCA1C20088B01A0030009ED08A0030009D317430B000960D380E260C380E5480E66E3E4C0E506D314430B0009D414D314432B00090009FFFF82D8FFFFE80000000EE4000C0091FFFF82E0000070000000FFFFFFFFEC26FFFFD004FFFFD804FFFFE320FFFFF733FFFFF740FFFFF784000004840000100000000100000004C200000924000000404D57FFFFFF0000005A594A3245413030305A595635303530");
    public static byte[] Image => image.ToArray();
    public static IReadOnlyList<RamSelfTestStep> Steps { get; } = Build().AsReadOnly();
    private static List<RamSelfTestStep> Build()
    {
        if (image.Length != 752 || RomStorage.Hash(image) != ImageSha256)
            throw new InvalidDataException("Write-enable control image integrity failed.");
        return FixedRamScript.Build(image, [0x7A,0xC6,0x4D,0x57]);
    }
}

// Immutable concatenation: exact live boot binding must precede the fixed RAM script.
public sealed class WriteEnableControlPlan(RecoveryBindingPlan binding) : IReadOnlyList<RamSelfTestStep>
{
    public RecoveryBindingPlan Binding { get; } = binding ?? throw new ArgumentNullException(nameof(binding));
    public int Count => Binding.Count + Zyj2WriteEnableControlPolicy.Steps.Count;
    public RamSelfTestStep this[int index] => index < 0 || index >= Count
        ? throw new ArgumentOutOfRangeException(nameof(index))
        : index < Binding.Count ? Binding[index] : Zyj2WriteEnableControlPolicy.Steps[index-Binding.Count];
    public IEnumerator<RamSelfTestStep> GetEnumerator()
    { for (int index=0;index<Count;index++) yield return this[index]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record WriteEnableControlResult(int UploadedBytes, int VerifiedBytes, byte Status,
    byte DuringFccs, byte AfterFccs, byte ObservationFlags)
{
    public bool Passed => UploadedBytes == 752 && VerifiedBytes == 752 && Status == 0 && DuringFccs == 0x80 && AfterFccs == 0 && ObservationFlags == 31;
    public bool RestorationConfirmed => (ObservationFlags & 29) == 29 && AfterFccs == 0 && Status != 8;
    public string StatusDescription => Status switch
    {
        0 => "Write-enable transition and restoration checks passed",
        1 => "Resident hardware class is not SH7058S",
        2 => "Software ID no longer matches the intact ZYJ2 spare",
        3 => "Native counter guard is invalid or not below 100",
        4 => "Controller protection, selection, mapping, key or RAM-emulation gate failed",
        5 => "Live CAN timing does not establish the fixed settling-loop clock",
        6 => "Port-D mux/direction or PD8 input is not the expected cold state",
        7 => "Enabled PD8/FWE observation did not pass",
        8 => "GPIO/controller restoration could not be confirmed; power removal required",
        _ => "Unknown write-enable control status"
    };
}

public sealed class Zyj2WriteEnableControl
{
    public WriteEnableControlResult Run(IWriteEnableControlChannel channel, Action<string> record,
        CancellationToken cancel = default, Action<WriteEnableControlResult>? observeResult = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int uploaded=0,verified=0; WriteEnableControlResult? result=null;
        record("fixed-write-enable-control-started-no-controller-write-erase-or-programming");
        for (int index=0;index<Zyj2WriteEnableControlPolicy.Steps.Count;index++)
        {
            cancel.ThrowIfCancellationRequested();
            if(elapsed.Elapsed >= TimeSpan.FromSeconds(60)) throw new TransportException("Write-enable control script deadline exceeded. No retry was attempted.");
            var step=Zyj2WriteEnableControlPolicy.Steps[index]; channel.SendControlStep(index);
            if(step.Operation==RamSelfTestOperation.Upload) uploaded+=step.Payload[1]&7;
            if(!step.ExpectsReply) continue;
            var frame=Zyj2RamSelfTest.Receive(channel,step,cancel,maximumFrames:256);
            if(step.Operation==RamSelfTestOperation.VerifyByte && ++verified==Zyj2WriteEnableControlPolicy.Image.Length)
                record("every-write-enable-control-image-byte-verified-before-execution");
            if(step.Operation==RamSelfTestOperation.ExecuteFixedTest)
            {
                result=new(uploaded,verified,frame.Data[4],frame.Data[5],frame.Data[6],frame.Data[7]);
                observeResult?.Invoke(result); record("write-enable-control-result-received");
                if (result.Status >= 8 || ((result.ObservationFlags & 1) != 0 && !result.RestorationConfirmed))
                    throw new TransportException("Write-enable restoration is uncertain. Remove ECU positive power; no monitor request or retry will be sent.");
            }
        }
        if(result is null || uploaded!=Zyj2WriteEnableControlPolicy.Image.Length || verified!=uploaded)
            throw new TransportException("Write-enable control evidence is incomplete.");
        record("monitor-return-confirmed-after-write-enable-control");
        return result;
    }
}
