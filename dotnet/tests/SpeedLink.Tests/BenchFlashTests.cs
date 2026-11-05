using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.J2534;

namespace SpeedLink.Tests;

public class BenchFlashTests
{
    private const string Calibration = "L3M6EG000L3T60A1", Chassis = "GG3P123456";
    private static readonly byte[] Stock = Fixture.Image(Fixture.Profiles.Single(p => p.Identity == Calibration));
    private static BenchFlashPlan Plan(TempFolder temp, string? image = null)
    {
        string backup = TestBackup(temp.Path, Stock, Calibration, Chassis);
        return SimPlan(image ?? backup, backup);
    }
    private static BenchFlashPlan SimPlan(string image, string backup) => BenchFlashPlan.LoadSimulation(image, backup, Fixture.Inspector(), Fixture.TuningGuard);
    private static BenchRecoveryPlan SimRecovery(string journal, string backup) => BenchRecoveryPlan.LoadSimulation(journal, backup, Fixture.Inspector(), Fixture.TuningGuard);
    [Fact]
    public void flash_allowlist_covers_all_bundled_gen1_definitions_and_rejects_gen2()
    {
        var bundled = TuningDefinition.Known.Select(definition => Fixture.Profiles.Single(profile =>
            profile.Calibration == definition.Calibration && profile.Identity == definition.Identity)).ToArray();
        Assert.Equal(9, bundled.Count(profile => profile.Family == "Gen1"));
        Assert.All(bundled, profile => Assert.Equal(profile.Family == "Gen1",
            BenchFlashWorkflow.IsNativeWriterVerified(profile.Calibration)));

        using var temp = new TempFolder();
        var gen2 = bundled.Single(profile => profile.Family == "Gen2");
        string backup = TestBackup(temp.Path, Fixture.Image(gen2), gen2.Identity, Chassis);
        Assert.Throws<InvalidDataException>(() => SimPlan(backup, backup));

        var simulated = Plan(temp);
        bool opened = false;
        Assert.Throws<InvalidDataException>(() => new BenchFlashWorkflow().Execute(simulated, () =>
        {
            opened = true;
            return new PeerState().Open();
        }, temp.Path, evidenceMode: "hardware"));
        Assert.False(opened);
    }

    private sealed class Progress(Action<BenchFlashProgress> action) : IProgress<BenchFlashProgress> { public void Report(BenchFlashProgress value) => action(value); }
    private sealed class PeerState(byte[]? fixture = null)
    {
        public byte[] Memory = (fixture ?? Stock).ToArray();
        public List<byte[]> Requests = [];
        public List<Peer> Peers = [];
        public Func<byte[], byte[]?, byte[]?>? Fault;
        public int Cursor = 0x8000;
        public bool WrongIdentity, BadBoot, Running, ReadbackMismatch;
        public int Voltage = 13500;
        public int Resets;
        public bool ZeroSeed;
        public string ReportedCalibration = Calibration;
        public string? Vehicle;
        public bool BootOnly;
        public byte DownloadLimitLowByte = 0xFF;
        public IBenchFlashTransport Open() { var p = new Peer(this); Peers.Add(p); return p; }
    }
    private sealed class Peer(PeerState state) : IBenchFlashTransport
    {
        public string Mode => "simulator";
        public string Adapter => "SYNTHETIC private flash peer";
        public int ReadChunkSize => 3968;
        public bool ReadAttempted { get; set; }
        public string? CloseWarning => null;
        public int BatteryMillivolts => state.Voltage;
        public bool Disposed;
        public void Initialize() { }
        public byte[] Exchange(byte[] p)
        {
            if (Disposed) throw new TransportException("Closed peer.");
            state.Requests.Add(p.ToArray()); byte[]? r = null;
            if (state.BootOnly && p[0] is 9 or 1 or 0x23) throw new NegativeResponseException(p[0], 0x11);
            if (p.AsSpan().SequenceEqual(new byte[] { 9, 2 }))
                r = [0x49, 2, 1, .. Encoding.ASCII.GetBytes(state.WrongIdentity ? "GG3P999999" : state.Vehicle ?? Chassis)];
            if (p.AsSpan().SequenceEqual(new byte[] { 9, 4 })) r = [0x49, 4, 1, .. Encoding.ASCII.GetBytes(state.ReportedCalibration)];
            if (p.AsSpan().SequenceEqual(new byte[] { 1, 12 })) r = [0x41, 0x0C, (byte)(state.Running ? 1 : 0), 0];
            if (p.Length == 2 && p[0] == 0x10) r = [0x50, p[1]];
            if (p.AsSpan().SequenceEqual(new byte[] { 0x27, 1 })) r = state.ZeroSeed ? [0x67, 1, 0, 0, 0] : [0x67, 1, 0x12, 0x34, 0x56];
            if (p.Length == 5 && p[0] == 0x27 && p[1] == 2)
            { Assert.Equal(SecurityAccess.SeedKey(new byte[] { 0x12, 0x34, 0x56 }), p[2..]); r = [0x67, 2]; }
            if (p.Length == 7 && p[0] == 0x23)
            {
                int at = (int)BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(1)), n = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(5));
                Assert.InRange(n, 1, 3968); Assert.InRange(at, 0, 0x100000 - n);
                r = [0x63, .. state.Memory.AsSpan(at, n)];
                if ((state.BadBoot && at == 0) || (state.ReadbackMismatch && state.Resets > 0 && at == 0xF800)) r[^1] ^= 1;
            }
            if (p.AsSpan().SequenceEqual(new byte[] { 0xB1, 0, 0xB2, 0 }))
            { state.Memory.AsSpan(0x8000).Fill(0xFF); state.Cursor = 0x8000; r = [0xF1, 0, 0xB2]; }
            if (p[0] == 0x34) { Assert.Equal(Convert.FromHexString("3400008000000F8000"), p); r = [0x74, 0x0F, state.DownloadLimitLowByte]; }
            if (p[0] == 0x36)
            { Assert.Equal(3969, p.Length); p.AsSpan(1).CopyTo(state.Memory.AsSpan(state.Cursor)); state.Cursor += 3968; r = [0x76]; }
            if (p.AsSpan().SequenceEqual(new byte[] { 0x37 })) { Assert.Equal(0x100000, state.Cursor); r = [0x77]; }
            if (p.AsSpan().SequenceEqual(new byte[] { 0x11, 1 })) { state.Resets++; state.BootOnly = false; r = [0x51]; }
            r = state.Fault is null ? r : state.Fault(p, r);
            return r ?? throw new TransportException("Synthetic missing response after possible acceptance.");
        }
        public void Dispose() => Disposed = true;
    }

    private static string TestBackup(string vault, byte[] rom, string calibration, string chassis)
    {
        var store = new BackupStore();
        var created = store.CreateOperation(vault, "simulator", "read_backup");
        using var journal = created.Journal;
        var identity = new EcuIdentity("simulator", "fixture", null, calibration) { ChassisId = chassis };
        journal.Append("identity", new() { ["identity"] = identity.ToManifest() });
        foreach (var capture in new[] { "pass1.read", "pass2.read" })
            DurableStorage.NewFile(store.FileSystem, Path.Combine(created.Folder, capture), rom);
        string hash = RomInspector.Hash(rom);
        var path = store.Seal(created.Folder, rom, identity, Fixture.Inspector().Inspect(rom), hash, hash, "simulator");
        journal.Append("completed", new() { ["sha256"] = hash });
        return path;
    }

    [Theory]
    [InlineData("L33XEH000")] [InlineData("L35HEH000")] [InlineData("L38KEH000")]
    [InlineData("L39SED000")] [InlineData("L39SEE000")] [InlineData("L39SEF000")]
    [InlineData("L3M6EG000")] [InlineData("L3M7EF000")] [InlineData("L3P4EM000")]
    public void nine_gen1_profiles_support_per_ecu_binding_edit_flash_and_recovery_in_simulation(string calibration)
    {
        var profile = Fixture.Profiles.Single(p => p.Calibration == calibration);
        var definition = TuningDefinition.Known.Single(d => d.Calibration == calibration);
        var table = definition.Tables.Single(t => t.Name == "Absolute Rev Limiter - 1");
        byte[] original = Fixture.Image(profile);
        BinaryPrimitives.WriteSingleBigEndian(original.AsSpan(table.Address, 4), 8000f);
        original = Fixture.Inspector().CorrectedCopy(original);
        using var temp = new TempFolder();
        const string chassis = "GG3P123456";
        var peer = new PeerState(original)
        {
            ReportedCalibration = profile.Identity, Vehicle = chassis,
            DownloadLimitLowByte = calibration is "L35HEH000" or "L39SED000" or "L39SEF000" ? (byte)0xF9 : (byte)0xFF
        };
        var backup = TestBackup(temp.Path, original, calibration, chassis);
        var editor = new RomEditor(original, backup, Fixture.Inspector(), Fixture.TuningGuard);
        editor.Apply([new CellEdit(table, 0, 7900)], "Synthetic write matrix");
        string edited = Path.Combine(temp.Path, "edited.bin");
        editor.SaveNew(edited);
        var plan = SimPlan(edited, backup);
        Assert.Equal(calibration, plan.Profile.Calibration);
        Assert.True(plan.ChangedBytes > 0);
        var normal = new BenchFlashWorkflow(resetWait: _ => { });
        normal.Execute(plan, peer.Open, temp.Path, evidenceMode: "simulator");
        Assert.Equal(File.ReadAllBytes(edited), peer.Memory);

        var interrupted = new BenchFlashWorkflow(resetWait: _ => { });
        peer.Fault = (request, reply) => request[0] == 0x36 ? null : reply;
        Assert.Throws<TransportException>(() => interrupted.Execute(plan, peer.Open, temp.Path, evidenceMode: "simulator"));
        Assert.Equal(BenchFlashStage.RecoveryRequired, interrupted.Stage);
        string prior = Path.Combine(interrupted.RunFolder!, "journal.jsonl");
        Assert.Throws<InvalidDataException>(() => SimRecovery(Path.Combine(normal.RunFolder!, "journal.jsonl"), backup));
        string otherBackup = TestBackup(temp.Path, original, calibration, "GG3P654321");
        Assert.Throws<InvalidDataException>(() => SimRecovery(prior, otherBackup));
        var recover = SimRecovery(prior, backup);
        peer.Fault = null;
        int sent = peer.Requests.Count;
        Assert.Throws<InvalidDataException>(() => new BenchFlashWorkflow().Recover(recover, false, peer.Open, temp.Path, evidenceMode: "simulator"));
        Assert.Equal(sent, peer.Requests.Count);
        Assert.Throws<TransportException>(() => new BenchFlashWorkflow().Recover(recover, true, peer.Open, temp.Path, evidenceMode: "simulator"));
        Assert.DoesNotContain(peer.Requests.Skip(sent), p => p[0] is 0xB1 or 0x36);
        peer.BootOnly = true;
        var recovery = new BenchFlashWorkflow(resetWait: _ => { });
        var result = recovery.Recover(recover, true, peer.Open, temp.Path, evidenceMode: "simulator");
        Assert.Equal(BenchFlashStage.Complete, recovery.Stage);
        Assert.Equal(original, File.ReadAllBytes(result.Readback));
        Assert.Equal(original, peer.Memory);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(result.Readback)!, "pass2.read")));
    }
    [Fact]
    public void stopping_after_one_acknowledged_block_leaves_the_retained_loader_and_a_recoverable_journal()
    {
        using var temp = new TempFolder();
        var original = Stock;
        var backup = TestBackup(temp.Path, original, Calibration, Chassis);
        var peer = new PeerState { Vehicle = "GG3P123456" };
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        var stop = new Progress(p =>
        {
            if (p.Stage == BenchFlashStage.Transfer && p.AcknowledgedBytes > 0)
                throw new OperationCanceledException("Planned stop before the next block.");
        });
        var plan = SimPlan(backup, backup);
        Assert.Throws<OperationCanceledException>(() => flow.Execute(plan, peer.Open, temp.Path, stop, evidenceMode: "simulator"));
        Assert.Equal(BenchFlashStage.RecoveryRequired, flow.Stage);
        Assert.Equal(BenchFlashPlan.Chunk, flow.AcknowledgedBytes);
        Assert.Single(peer.Requests, p => p[0] == 0x36);
        Assert.DoesNotContain(peer.Requests, p => p[0] is 0x37 or 0x11);
        Assert.Equal(original[..BenchFlashPlan.Start], peer.Memory[..BenchFlashPlan.Start]);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, peer.Memory[^2..]);
        var recover = SimRecovery(Path.Combine(flow.RunFolder!, "journal.jsonl"), backup);
        peer.BootOnly = true;
        var result = new BenchFlashWorkflow(resetWait: _ => { }).Recover(recover, true, peer.Open, temp.Path, evidenceMode: "simulator");
        Assert.Equal(original, File.ReadAllBytes(result.Readback));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(result.Readback)!, "pass2.read")));
    }

    private sealed class SimulatedVLinker(FakeSerial serial) : IBenchFlashTransport
    {
        private readonly VLinkerBenchFlashTransport link = new(serial, new TestClock());
        public string Mode => "simulator";
        public string Adapter => link.Adapter;
        public int ReadChunkSize => link.ReadChunkSize;
        public bool ReadAttempted { get => link.ReadAttempted; set => link.ReadAttempted = value; }
        public int BatteryMillivolts => link.BatteryMillivolts;
        public string? CloseWarning => link.CloseWarning;
        public void Initialize() => link.Initialize();
        public byte[] Exchange(byte[] request) => link.Exchange(request);
        public void Dispose() => link.Dispose();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void vlinker_stock_flash_uses_full_blocks_and_verifies_each_selected_readback(bool singleReadback)
    {
        using var temp = new TempFolder();
        var state = new PeerState();
        var serialLinks = new List<FakeSerial>();
        IBenchFlashTransport Open()
        {
            var peer = new Peer(state);
            var serial = new FakeSerial { Segmented = true, ResponseOverride = peer.Exchange };
            serialLinks.Add(serial);
            return new SimulatedVLinker(serial);
        }
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        var result = flow.Execute(Plan(temp), Open, temp.Path, evidenceMode: "simulator", singleReadback: singleReadback);
        Assert.Equal(BenchFlashStage.Complete, flow.Stage);
        Assert.Equal(BenchFlashPlan.Length, flow.AcknowledgedBytes);
        Assert.Equal(2, serialLinks.Count);
        Assert.All(serialLinks, serial => { Assert.False(serial.IsOpen); Assert.Equal(115200, serial.BaudRate); });
        var commands = serialLinks[0].Writes.Select(Encoding.ASCII.GetString).ToArray();
        var blockIndexes = commands.Select((value, index) => (value, index))
            .Where(entry => entry.value == "STPX L:3969,T:10000\r").Select(entry => entry.index).ToArray();
        Assert.Equal(256, blockIndexes.Length);
        var original = Stock;
        for (int block = 0; block < blockIndexes.Length; block++)
        {
            string data = commands[blockIndexes[block] + 1];
            Assert.Equal("36" + Convert.ToHexString(original.AsSpan(BenchFlashPlan.Start + block * BenchFlashPlan.Chunk, BenchFlashPlan.Chunk)) + "\r", data);
        }
        Assert.Equal(original, File.ReadAllBytes(result.Readback));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Folder, "verified-result.json")));
        Assert.Equal(singleReadback ? 1 : 2, report.RootElement.GetProperty("readbackPasses").GetInt32());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void complete_stock_write_and_two_pass_readback(bool zeroSeed)
    {
        using var temp = new TempFolder(); var state = new PeerState { ZeroSeed = zeroSeed }; var plan = Plan(temp);
        var stages = new List<BenchFlashProgress>(); var flow = new BenchFlashWorkflow(resetWait: _ => { });
        var result = flow.Execute(plan, state.Open, temp.Path, new Progress(stages.Add), evidenceMode: "simulator");
        Assert.Equal(BenchFlashStage.Complete, flow.Stage); Assert.Equal(0xF8000, flow.AcknowledgedBytes);
        Assert.Equal(256, state.Requests.Count(r => r[0] == 0x36)); Assert.Single(state.Requests, r => r[0] == 0xB1);
        Assert.Equal(Stock, state.Memory); Assert.All(state.Peers, p => Assert.True(p.Disposed));
        Assert.True(new BackupStore().InspectJournal(Path.Combine(result.Folder, "journal.jsonl")).Complete);
        Assert.True(File.Exists(Path.Combine(result.Folder, "verified-result.json")));
        Assert.Equal(1, stages[^1].Fraction);
        Assert.All(stages.Where(p => p.Stage is BenchFlashStage.Erasing or BenchFlashStage.Transfer or BenchFlashStage.Resetting), p => Assert.False(p.CanCancel));
        Assert.DoesNotContain(Convert.ToHexString(state.Requests.First(r => r[0] == 0x36)), File.ReadAllText(Path.Combine(result.Folder, "journal.jsonl")));
        Assert.Throws<InvalidOperationException>(() => flow.Execute(plan, state.Open, temp.Path));
    }
    [Fact]
    public void complete_verified_calibration_edit_and_original_restore()
    {
        using var temp = new TempFolder(); byte[] modified = Stock.ToArray();
        BinaryPrimitives.WriteSingleBigEndian(modified.AsSpan(0x81B4, 4), 7900f);
        modified = Fixture.Inspector().CorrectedCopy(modified);
        string image = Path.Combine(temp.Path, "SYNTHETIC_CALIBRATION.bin"); File.WriteAllBytes(image, modified);
        var plan = Plan(temp, image); Assert.True(plan.ChangedBytes > 0);
        var state = new PeerState(); var flow = new BenchFlashWorkflow(resetWait: _ => { });
        var edit = flow.Execute(plan, state.Open, Path.Combine(temp.Path, "edit"), evidenceMode: "simulator", singleReadback: true);
        Assert.Equal(modified, state.Memory);
        Assert.Equal(modified, File.ReadAllBytes(edit.Readback));
        Assert.Equal("pass1.read", Path.GetFileName(edit.Readback));
        var readbackFolder = Path.GetDirectoryName(edit.Readback)!;
        Assert.False(File.Exists(Path.Combine(readbackFolder, "backup.json")));
        Assert.False(File.Exists(Path.Combine(readbackFolder, "pass2.read")));
        var journal = new BackupStore().InspectJournal(Path.Combine(readbackFolder, "journal.jsonl"));
        Assert.True(journal.Complete);
        Assert.Equal("verify_known_image", journal.Operation);
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(edit.Folder, "verified-result.json")));
        Assert.Equal(1, evidence.RootElement.GetProperty("readbackPasses").GetInt32());
        Assert.False(evidence.RootElement.GetProperty("twoMatchingPasses").GetBoolean());
        var restore = new BenchFlashWorkflow(resetWait: _ => { });
        restore.Execute(Plan(temp), state.Open, Path.Combine(temp.Path, "restore"), evidenceMode: "simulator");
        Assert.Equal(Stock, state.Memory);
    }
    [Theory]
    [InlineData("identity")] [InlineData("running")] [InlineData("low_voltage")] [InlineData("high_voltage")] [InlineData("boot")]
    public void preflight_failure_never_erases(string fault)
    {
        using var temp = new TempFolder(); var state = new PeerState { WrongIdentity = fault == "identity", Running = fault == "running", BadBoot = fault == "boot" };
        state.Voltage = fault == "low_voltage" ? 11999 : fault == "high_voltage" ? 15001 : 13500;
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator"));
        Assert.False(flow.DestructiveRequestIssued); Assert.Equal(BenchFlashStage.Failed, flow.Stage);
        Assert.DoesNotContain(state.Requests, r => r[0] is 0xB1 or 0x34 or 0x36 or 0x11);
        Assert.All(state.Peers, p => Assert.True(p.Disposed));
    }
    [Theory]
    [InlineData(0xB1)] [InlineData(0x34)] [InlineData(0x36)] [InlineData(0x37)] [InlineData(0x11)]
    public void ambiguous_acceptance_never_repeats_or_resets(int service)
    {
        using var temp = new TempFolder(); var state = new PeerState { Fault = (p, r) => p[0] == service ? null : r };
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator"));
        Assert.True(flow.DestructiveRequestIssued); Assert.Equal(BenchFlashStage.RecoveryRequired, flow.Stage);
        Assert.Single(state.Requests, r => r[0] == service);
        Assert.False(new BackupStore().InspectJournal(Path.Combine(flow.RunFolder!, "journal.jsonl")).Complete);
        Assert.All(state.Peers, p => Assert.True(p.Disposed));
        if (service != 0x11) Assert.Equal(0, state.Resets);
    }
    [Fact]
    public void malformed_transfer_ack_does_not_advance_progress()
    {
        using var temp = new TempFolder(); var state = new PeerState { Fault = (p, r) => p[0] == 0x36 ? [0x76, 0] : r };
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator"));
        Assert.Equal(0, flow.AcknowledgedBytes); Assert.Single(state.Requests, r => r[0] == 0x36);
    }
    [Fact]
    public void malformed_payload_sized_acknowledgement_is_omitted_from_diagnostic_text()
    {
        using var temp = new TempFolder(); var payload = Stock.AsSpan(0x8000, 1024).ToArray();
        var state = new PeerState { Fault = (p, r) => p[0] == 0x36 ? [0x76, .. payload] : r };
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator"));
        string journal = File.ReadAllText(Path.Combine(flow.RunFolder!, "journal.jsonl"));
        Assert.DoesNotContain(Convert.ToHexString(payload), journal); Assert.Contains("76 [payload omitted]", journal);
        Assert.Equal(BenchFlashStage.RecoveryRequired, flow.Stage); Assert.Equal(0, flow.AcknowledgedBytes);
    }
    [Fact]
    public void cancel_before_erase_is_safe_and_cancel_after_erase_does_not_interrupt_transfer()
    {
        foreach (bool after in new[] { false, true })
        {
            using var temp = new TempFolder(); using var cancel = new CancellationTokenSource(); var state = new PeerState();
            var flow = new BenchFlashWorkflow(resetWait: _ => { });
            Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, new Progress(p =>
            { if (p.Stage == (after ? BenchFlashStage.Transfer : BenchFlashStage.ProgrammingSession)) cancel.Cancel(); }), cancel.Token, "simulator"));
            Assert.Equal(after ? 256 : 0, state.Requests.Count(r => r[0] == 0x36));
            Assert.Equal(after ? BenchFlashStage.VerificationRequired : BenchFlashStage.Stopped, flow.Stage);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void readback_mismatch_cannot_report_success(bool singleReadback)
    {
        using var temp = new TempFolder(); var state = new PeerState { ReadbackMismatch = true }; var flow = new BenchFlashWorkflow(resetWait: _ => { });
        var error = Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator", singleReadback: singleReadback));
        Assert.Equal(BenchFlashStage.VerificationRequired, flow.Stage);
        Assert.False(File.Exists(Path.Combine(flow.RunFolder!, "verified-result.json")));
        if (singleReadback)
        {
            Assert.Contains("0x00F800", error.Message);
            var capture = Assert.Single(Directory.GetFiles(flow.RunFolder!, "pass1.read", SearchOption.AllDirectories));
            Assert.Equal(17L * 3968, new FileInfo(capture).Length);
            Assert.False(new BackupStore().InspectJournal(Path.Combine(Path.GetDirectoryName(capture)!, "journal.jsonl")).Complete);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void lost_post_reset_verification_can_be_repeated_read_only(bool singleReadback)
    {
        using var temp = new TempFolder();
        var state = new PeerState();
        var plan = Plan(temp);
        state.Fault = (request, reply) => state.Resets > 0 && request[0] == 0x23 ? null : reply;
        var flow = new BenchFlashWorkflow(resetWait: _ => { });
        Assert.Throws<TransportException>(() => flow.Execute(plan, state.Open, temp.Path, evidenceMode: "simulator",
            singleReadback: singleReadback));
        Assert.Equal(BenchFlashStage.VerificationRequired, flow.Stage);
        Assert.False(File.Exists(Path.Combine(flow.RunFolder!, "verified-result.json")));
        Assert.Single(state.Requests, request => request[0] == 0xB1);
        Assert.Single(state.Requests, request => request[0] == 0x11);

        state.Fault = null;
        int priorRequests = state.Requests.Count;
        byte[] expected = Stock;
        using var fresh = state.Open();
        fresh.Initialize();
        string capture = Fixture.Reader().VerifyKnownImage(fresh, plan.Identity,
            Path.Combine(temp.Path, "separate-verification"), expected);
        Assert.Equal(expected, File.ReadAllBytes(capture));
        Assert.All(state.Requests.Skip(priorRequests), request => DiagnosticProtocol.CheckReadOnly(request, BenchFlashPlan.Chunk));
        Assert.True(new BackupStore().InspectJournal(Path.Combine(Path.GetDirectoryName(capture)!, "journal.jsonl")).Complete);
        Assert.Single(state.Requests, request => request[0] == 0xB1);
        Assert.Single(state.Requests, request => request[0] == 0x11);
    }
    [Fact]
    public void invalid_image_and_changed_boot_rejected_without_adapter_access()
    {
        using var temp = new TempFolder(); var original = Stock;
        foreach (int at in new[] { 0x414, 0x70000 })
        {
            byte[] image = original.ToArray(); image[at] ^= 1; string path = Path.Combine(temp.Path, at + ".bin"); File.WriteAllBytes(path, image);
            Assert.Throws<InvalidDataException>(() => Plan(temp, path));
        }
        File.WriteAllBytes(Path.Combine(temp.Path, "short.bin"), original[..100]);
        Assert.Throws<InvalidDataException>(() => Plan(temp, Path.Combine(temp.Path, "short.bin")));
    }
    [Fact]
    public void permit_rejects_unplanned_replaced_and_repeated_requests()
    {
        var permit = new BenchRequestPermit(); Assert.Throws<TransportException>(() => permit.Consume([0x37]));
        permit.Prepare([0x37]); Assert.Throws<TransportException>(() => permit.Consume([0x11, 1]));
        permit.Consume([0x37]); Assert.Throws<TransportException>(() => permit.Consume([0x37]));
    }
    [Fact]
    public void durable_journal_failure_before_dispatch_blocks_erase()
    {
        using var temp = new TempFolder(); var state = new PeerState();
        var disk = new TestFileSystem { Fault = "disk_full", Predicate = (p, data) => p.EndsWith("journal.jsonl", StringComparison.Ordinal) && Encoding.ASCII.GetString(data).Contains("destructive_boundary", StringComparison.Ordinal) };
        var flow = new BenchFlashWorkflow(new BackupStore(disk), _ => { });
        Assert.ThrowsAny<Exception>(() => flow.Execute(Plan(temp), state.Open, temp.Path, evidenceMode: "simulator"));
        Assert.True(disk.Fired); Assert.False(flow.DestructiveRequestIssued); Assert.DoesNotContain(state.Requests, r => r[0] == 0xB1);
    }
}
