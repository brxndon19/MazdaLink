using System.Text;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public class BenchRecoveryTests
{
    private sealed class Channel : IBenchMonitorChannel
    {
        public List<string> Calls { get; } = [];
        public Queue<RecoveryFrame> Replies { get; } = new();
        public string? Fail { get; init; }
        public string? CleanupWarning => null;
        private void Call(string name) { Calls.Add(name); if (Fail == name) throw new TransportException(name); }
        public void Open() => Call("open");
        public void StartEntryPulse() => Call("start");
        public void StopEntryPulse() => Call("stop");
        public void ClearReceive() => Call("clear");
        public void RequestIdentity() => Call("identity");
        public RecoveryFrame? Read(int milliseconds) { Assert.InRange(milliseconds, 1, 25); Call("read"); return Replies.TryDequeue(out var frame) ? frame : null; }
        public void Dispose() => Call("dispose");
    }
    private sealed class Live(string identity, ushort rpm = 0, string? secondIdentity = null) : IReadTransport
    {
        public List<string> Requests { get; } = [];
        private int identities;
        public byte[] Exchange(byte[] request)
        {
            Requests.Add(Convert.ToHexString(request));
            return request[0] switch
            {
                9 => [0x49, 4, 1, .. Encoding.ASCII.GetBytes(++identities > 1 ? secondIdentity ?? identity : identity)],
                1 => [0x41, 0x0C, (byte)(rpm >> 8), (byte)rpm],
                _ => throw new Exception("Unexpected unlock or memory operation")
            };
        }
        public void Dispose() { }
    }
    private static RecoveryFrame Identity(uint status = 0x100) => new(0x21, status, [0x7A, 0xC6, 0x50, 0x79, 0x77, 0x30, 0x58, 0x41]);

    [Fact]
    public void EntryStopsBeforeOneIdentificationAndNeverUploadsOrExecutes()
    {
        using var channel = new Channel(); channel.Replies.Enqueue(Identity());
        var events = new List<string>();
        new Zyj2BenchRecovery().IdentifyMonitor(channel, events.Add, (time, token) =>
        {
            Assert.Equal(20000, time); Assert.Equal(new[] { "open", "start" }, channel.Calls);
        });
        Assert.Equal(new[] { "open", "start", "stop", "clear", "identity", "read" }, channel.Calls);
        Assert.Equal("monitor-identified-no-flash-operation", events[^1]);
        Assert.DoesNotContain(typeof(IBenchMonitorChannel).GetMethods(), m =>
            new[] { "Write", "Send", "Upload", "Erase", "Reset", "Execute" }.Contains(m.Name));
    }
    [Theory]
    [InlineData("start")][InlineData("stop")][InlineData("clear")][InlineData("identity")]
    public void DriverFailuresDoNotRetryOrContinueToSuccess(string fail)
    {
        using var channel = new Channel { Fail = fail }; channel.Replies.Enqueue(Identity());
        var events = new List<string>();
        Assert.Throws<TransportException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel, events.Add, (_, _) => { }));
        Assert.Equal(1, channel.Calls.Count(c => c == fail));
        Assert.DoesNotContain("monitor-identified-no-flash-operation", events);
        if (fail == "start") Assert.Equal(new[] { "open", "start", "stop" }, channel.Calls);
        if (fail == "stop") Assert.DoesNotContain("identity", channel.Calls);
    }
    [Fact]
    public void CancellationStopsPeriodicTrafficBeforeAnyIdentificationRequest()
    {
        using var channel = new Channel(); using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel, _ => { },
            (_, _) => cancel.Cancel(), cancel.Token));
        Assert.Equal(new[] { "open", "start", "stop" }, channel.Calls);
    }
    [Fact]
    public void EvidenceWriteFailureCannotStartPeriodicTraffic()
    {
        using var channel = new Channel();
        Assert.Throws<IOException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel, stage =>
        { if (stage == "entry-window-armed") throw new IOException("Disk failure"); }, (_, _) => { }));
        Assert.Equal(new[] { "open" }, channel.Calls);
    }
    [Fact]
    public void EchoesAndForeignFramesCannotStandInForMonitorIdentification()
    {
        using var channel = new Channel();
        channel.Replies.Enqueue(Identity(0x101));
        channel.Replies.Enqueue(new(0x7E8, 0x100, [0x49, 4, 1]));
        channel.Replies.Enqueue(Identity());
        var events = new List<string>();
        new Zyj2BenchRecovery().IdentifyMonitor(channel, events.Add, (_, _) => { });
        Assert.Equal(3, channel.Calls.Count(c => c == "read"));
        Assert.Single(channel.Calls, c => c == "identity");
    }
    [Theory]
    [InlineData(0u)][InlineData(0x110u)][InlineData(0x200u)]
    public void StandardCanOrUnexpectedStatusCannotPass(uint status)
    {
        using var channel = new Channel(); channel.Replies.Enqueue(Identity(status));
        Assert.Throws<TransportException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel, _ => { }, (_, _) => { }));
        Assert.Single(channel.Calls, c => c == "identity");
    }
    [Fact]
    public void MalformedIdentityAndExcessTrafficFailWithoutAnotherRequest()
    {
        using var malformed = new Channel(); malformed.Replies.Enqueue(new(0x21, 0x100, [0x7A, 0xC6]));
        Assert.Throws<TransportException>(() => new Zyj2BenchRecovery().IdentifyMonitor(malformed, _ => { }, (_, _) => { }));
        using var noisy = new Channel();
        for (int i = 0; i < 65; i++) noisy.Replies.Enqueue(new(0x22, 0x100, []));
        Assert.Throws<TransportException>(() => new Zyj2BenchRecovery().IdentifyMonitor(noisy, _ => { }, (_, _) => { }));
        Assert.Single(noisy.Calls, c => c == "identity");
    }
    [Fact]
    public void WireGateAllowsOnlyItsTwoFixedNonProgrammingPayloads()
    {
        Zyj2RecoveryPolicy.CheckTransmit(Zyj2RecoveryPolicy.EntryRequest, true);
        Zyj2RecoveryPolicy.CheckTransmit(Zyj2RecoveryPolicy.IdentityRequest, false);
        foreach (string hex in new[] { "7AA0000000000000", "7AAE000000000000", "7A9CFFFF40000000", "7AB4000000000000", "FFC8000000000000", "3101FF00", "340044FFFF400000000100", "3601FF", "1101", "" })
        {
            Assert.Throws<TransportException>(() => Zyj2RecoveryPolicy.CheckTransmit(Convert.FromHexString(hex), true));
            Assert.Throws<TransportException>(() => Zyj2RecoveryPolicy.CheckTransmit(Convert.FromHexString(hex), false));
        }
        Assert.Throws<TransportException>(() => Zyj2RecoveryPolicy.CheckTransmit(Zyj2RecoveryPolicy.EntryRequest, false));
        Assert.Throws<TransportException>(() => Zyj2RecoveryPolicy.CheckTransmit(Zyj2RecoveryPolicy.IdentityRequest, true));
    }
    [Fact]
    public void BenchPreflightChecksExactIdentityAndStoppedEngineWithoutUnlock()
    {
        using var live = new Live(Zyj2RecoveryPolicy.SoftwareId);
        Zyj2RecoveryPolicy.RequireLiveTarget(live);
        Assert.Equal(new[] { "0904", "010C", "0904" }, live.Requests);
        using var wrong = new Live(TargetPolicy.SoftwareId);
        Assert.Throws<InvalidDataException>(() => Zyj2RecoveryPolicy.RequireLiveTarget(wrong));
        Assert.Equal(new[] { "0904" }, wrong.Requests);
        using var running = new Live(Zyj2RecoveryPolicy.SoftwareId, 4000);
        Assert.Throws<TransportException>(() => Zyj2RecoveryPolicy.RequireLiveTarget(running));
        using var changed = new Live(Zyj2RecoveryPolicy.SoftwareId, secondIdentity: TargetPolicy.SoftwareId);
        Assert.Throws<InvalidDataException>(() => Zyj2RecoveryPolicy.RequireLiveTarget(changed));
    }
    [Fact]
    public void SpoofingHeaderAndCalibrationDoesNotSatisfyTheBootCodeFingerprints()
    {
        byte[] rom = new byte[TargetPolicy.RomSize];
        "ZYJ2EA0W"u8.CopyTo(rom.AsSpan(0xC0000));
        Encoding.ASCII.GetBytes(Zyj2RecoveryPolicy.SoftwareId).CopyTo(rom, 0xC0091);
        "ZYC3"u8.CopyTo(rom.AsSpan(0x6200));
        var error = Assert.Throws<InvalidDataException>(() => Zyj2RecoveryPolicy.RequireBackup(new(rom, Zyj2RecoveryPolicy.SoftwareId)));
        Assert.Contains("monitor and resident loader", error.Message);
        Assert.Throws<InvalidDataException>(() => Zyj2RecoveryPolicy.RequireBackup(new(rom, TargetPolicy.SoftwareId)));
    }
    [Fact]
    public void MissingBackupAndUnlistedBenchArgumentsCannotOpenEitherBackend()
    {
        int opens = 0;
        var terminal = new Terminal(() => { opens++; throw new Exception("No hardware"); },
            new StringReader("BENCH ZYJ2\nSTART\n\n"), new StringWriter(), Path.GetTempPath(),
            () => { opens++; throw new Exception("No bench hardware"); });
        Assert.Equal(1, terminal.Execute(["bench-recovery-check", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "original.rom")]));
        Assert.Equal(1, terminal.Execute(["bench-recovery-check", "original.rom", "--ignore-backup"]));
        Assert.Equal(0, opens);
    }
    [Fact]
    public void CancellationBeforeEntryCannotOpenTheBenchChannel()
    {
        using var channel = new Channel(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel, _ => { }, cancel: cancel.Token));
        Assert.Empty(channel.Calls);
    }
    [Fact]
    public void CancellationWhileArmingCannotStartTheEntryPulse()
    {
        using var channel = new Channel(); using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new Zyj2BenchRecovery().IdentifyMonitor(channel,
            stage => { if (stage == "entry-window-armed") cancel.Cancel(); }, cancel: cancel.Token));
        Assert.Equal(new[] { "open" }, channel.Calls);
    }
}
