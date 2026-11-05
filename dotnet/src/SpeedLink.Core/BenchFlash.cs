// The programming workflow is separate from the read-only diagnostic transports.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

[assembly: InternalsVisibleTo("SpeedLink")]
[assembly: InternalsVisibleTo("SpeedLink.Tests")]

namespace SpeedLink.Core;

public interface IBenchFlashTransport : IReadTransport
{
    void Initialize();
    int BatteryMillivolts { get; }
    string? CloseWarning { get; }
}

public enum BenchFlashStage { Preflight, ProgrammingSession, Erasing, Download, Transfer, Finalizing, Resetting, Reconnecting, Readback, Complete, Stopped, Failed, RecoveryRequired, VerificationRequired, WrittenUnverified }
internal enum FlashEcuState { Normal, Recovery }
public sealed record BenchFlashProgress(BenchFlashStage Stage, string Message, double Fraction, int AcknowledgedBytes, bool CanCancel);
public sealed record BenchFlashResult(string Folder, string? Readback, string ImageSha256);

/// <summary>Immutable Gen 1 image/backup association, with ECU-specific boot bytes preserved.</summary>
public sealed class BenchFlashPlan
{
    public const int Start = 0x8000, Length = 0xF8000, Chunk = 3968;
    internal readonly byte[] Image, Baseline;
    public RomProfile Profile { get; }
    public string FullCalibration => Profile.Identity;
    internal string BackupMode { get; }
    internal RomInspector Inspector { get; }
    public EcuIdentity Identity { get; }
    public string ImageSha256 { get; }
    public string BackupSha256 { get; }
    public string BootSha256 { get; }
    public int ChangedBytes { get; }
    public string BackupPath { get; }
    private BenchFlashPlan(byte[] image, LoadedBackup backup, string backupPath, RomInspector inspector, TuningCodeGuard codeGuard)
    {
        Image = image.ToArray(); Baseline = backup.Data.ToArray(); BackupPath = Path.GetFullPath(backupPath);
        Inspector = inspector;
        Profile = inspector.ProfileFor(Baseline) ?? throw new InvalidDataException("The backup has no known ROM profile.");
        if (Profile.Family != "Gen1" || Profile.BootLength != Start || Profile.CorrectionAddress < Start)
            throw new InvalidDataException("Programming is limited to the supported Gen 1 ROM layout.");
        var definition = TuningDefinition.Known.SingleOrDefault(d => d.Calibration == Profile.Calibration && d.Identity == Profile.Identity)
            ?? throw new InvalidDataException("A complete bundled definition is required for this calibration.");
        foreach (var data in new[] { Image, Baseline })
        {
            var info = inspector.Inspect(data);
            if (info.Calibration != Profile.Calibration || !info.ProfileValid || info.ChecksumValid != true ||
                !definition.Matches(data) || !codeGuard.Matches(data, definition, Profile))
                throw new InvalidDataException("Image identity, layout, markers or checksum is invalid. Export a checksum-verified copy from the editor first.");
        }
        if (!Image.AsSpan(0, Start).SequenceEqual(Baseline.AsSpan(0, Start)))
            throw new InvalidDataException("The first 32 KiB, including ECU-specific data, must match the original exactly.");
        ValidateDefinedDifferences(definition);
        var id = backup.Manifest["identity"]?.AsObject() ?? throw new InvalidDataException("Missing backup identity.");
        BackupMode = backup.Manifest["mode"]!.GetValue<string>();
        Identity = ReadIdentity(id, BackupMode);
        if (!MatchesCalibration(Identity.Calibration) || string.IsNullOrWhiteSpace(Identity.VehicleIdentifier) ||
            Identity.Vin is not null && Identity.ChassisId is not null)
            throw new InvalidDataException("A matching calibration and one vehicle identifier are required in the baseline backup.");
        ImageSha256 = RomInspector.Hash(Image); BackupSha256 = RomInspector.Hash(Baseline);
        BootSha256 = RomInspector.Hash(Baseline.AsSpan(0, Start));
        for (int i = Start; i < Image.Length; i++) if (Image[i] != Baseline[i]) ChangedBytes++;
    }
    internal bool MatchesCalibration(string? reported) => reported == Profile.Calibration || reported == FullCalibration;
    internal bool MatchesLive(EcuIdentity live) => MatchesCalibration(live.Calibration) && live.Vin == Identity.Vin && live.ChassisId == Identity.ChassisId;
    internal string VehicleIdentityHash => RomInspector.Hash(Encoding.ASCII.GetBytes(Identity.VehicleIdentifierLabel + "\n" + Identity.VehicleIdentifier));

    public static BenchFlashPlan Load(string imagePath, string backupPath, string requiredMode = "hardware")
        => LoadCore(imagePath, backupPath, requiredMode, new(), new());

    internal static BenchFlashPlan LoadSimulation(string imagePath, string backupPath, RomInspector inspector, TuningCodeGuard codeGuard)
        => LoadCore(imagePath, backupPath, "simulator", inspector, codeGuard);

    private static BenchFlashPlan LoadCore(string imagePath, string backupPath, string requiredMode, RomInspector inspector, TuningCodeGuard codeGuard)
    {
        var store = new BackupStore();
        var plan = new BenchFlashPlan(File.ReadAllBytes(imagePath), store.Load(backupPath, requiredMode), backupPath, inspector, codeGuard);
        JsonObject? journalIdentity = null;
        var journal = store.InspectJournal(Path.Combine(Path.GetDirectoryName(plan.BackupPath)!, "journal.jsonl"), rows =>
        {
            var identities = rows.Where(r => r["event"]?.GetValue<string>() == "identity").ToArray();
            if (identities.Length == 1) journalIdentity = identities[0]["details"]?["identity"] as JsonObject;
        });
        if (!journal.Valid || !journal.Complete || journalIdentity is null ||
            !ReadIdentity(journalIdentity, requiredMode).MatchesVehicle(plan.Identity))
            throw new InvalidDataException("The backup identity does not match the verified read journal.");
        return plan;
    }

    private static EcuIdentity ReadIdentity(JsonObject identity, string mode)
    {
        string? Field(string name) => identity[name]?.GetValue<string>();
        return new(mode, "saved baseline", Field("vin"), Field("calibration")) { ChassisId = Field("chassisId") };
    }

    private void ValidateDefinedDifferences(TuningDefinition definition)
    {
        // The ROM program hash covers its known program region. Compare every byte as well,
        // including the earlier application region and bits adjacent to switch controls.
        var editable = new byte[RomInspector.RomSize];
        foreach (var table in definition.Tables)
        {
            if (table.Bits.Length == 0)
            {
                editable.AsSpan(table.Address, table.End - table.Address).Fill(0xFF);
                continue;
            }
            foreach (int bit in table.Bits)
            {
                int offset = table.BigEndian ? table.Width - 1 - bit / 8 : bit / 8;
                editable[table.Address + offset] |= (byte)(1 << (bit % 8));
            }
        }
        editable.AsSpan(Profile.CorrectionAddress, 4).Fill(0xFF);
        for (int address = Start; address < Image.Length; address++)
            if (((Image[address] ^ Baseline[address]) & ~editable[address]) != 0)
                throw new InvalidDataException($"The proposed ROM changes bytes outside the bundled definition at 0x{address:X6}.");
    }

    public object Summary(int readbackPasses = 2) => new { calibration = FullCalibration, imageSha256 = ImageSha256, backupSha256 = BackupSha256,
        bootSha256 = BootSha256, changedBytes = ChangedBytes, writeStart = Start, writeBytes = Length, chunkBytes = Chunk,
        transfers = Length / Chunk, readbackPasses, hardwareQualified = false, localOnly = true };
}

/// <summary>Only one explicitly prepared request may cross the private native write boundary.</summary>
public sealed class BenchRequestPermit
{
    private string? expected;
    public static void CheckRequest(ReadOnlySpan<byte> request)
    {
        bool allowed = request.SequenceEqual(new byte[] { 9, 2 }) || request.SequenceEqual(new byte[] { 9, 4 }) ||
            request.SequenceEqual(new byte[] { 1, 12 }) || request.SequenceEqual(new byte[] { 0x10, 0x87 }) ||
            request.SequenceEqual(new byte[] { 0x10, 0x85 }) || request.SequenceEqual(new byte[] { 0x27, 1 }) ||
            request.Length == 5 && request[0] == 0x27 && request[1] == 2 || request.SequenceEqual(new byte[] { 0xB1, 0, 0xB2, 0 }) ||
            request.SequenceEqual(Convert.FromHexString("3400008000000F8000")) || request.Length == BenchFlashPlan.Chunk + 1 && request[0] == 0x36 ||
            request.SequenceEqual(new byte[] { 0x37 }) || request.SequenceEqual(new byte[] { 0x11, 1 });
        if (request.Length == 7 && request[0] == 0x23)
        {
            uint at = BinaryPrimitives.ReadUInt32BigEndian(request.Slice(1, 4));
            int count = BinaryPrimitives.ReadUInt16BigEndian(request[5..]);
            allowed = count is > 0 and <= BenchFlashPlan.Chunk && (ulong)at + (uint)count <= RomInspector.RomSize;
        }
        if (!allowed) throw new TransportException("Request is outside the Gen 1 programming allowlist.");
    }
    public void Prepare(byte[] request)
    {
        if (expected is not null) throw new TransportException("A request is already outstanding.");
        expected = RomInspector.Hash(request);
    }
    public void Consume(byte[] request)
    {
        if (expected is null || RomInspector.Hash(request) != expected)
            throw new TransportException("Unplanned private programming request blocked before native I/O.");
        expected = null;
    }
    public void Clear() => expected = null;
}

/// <summary>Normal application programming and explicit full stock recovery. No cursor resume.</summary>
public sealed class BenchFlashWorkflow
{
    // Hardware access is limited to the profiles with a verified native command path.
    private static readonly Dictionary<string, ushort> NativeWriterProfiles = new(StringComparer.Ordinal)
    {
        ["L33XEH000"] = 0x0FFF, ["L35HEH000"] = 0x0FF9, ["L38KEH000"] = 0x0FFF,
        ["L39SED000"] = 0x0FF9, ["L39SEE000"] = 0x0FFF, ["L39SEF000"] = 0x0FF9,
        ["L3M6EG000"] = 0x0FFF, ["L3M7EF000"] = 0x0FFF, ["L3P4EM000"] = 0x0FFF
    };
    public static bool IsNativeWriterVerified(string calibration) => NativeWriterProfiles.ContainsKey(calibration);
    private readonly BackupStore store;
    private readonly Action<TimeSpan> resetWait;
    public BenchFlashStage Stage { get; private set; }
    public bool DestructiveRequestIssued { get; private set; }
    public int AcknowledgedBytes { get; private set; }
    public string? RunFolder { get; private set; }
    public BenchFlashWorkflow(BackupStore? store = null, Action<TimeSpan>? resetWait = null)
    { this.store = store ?? new(); this.resetWait = resetWait ?? (delay => Thread.Sleep(delay)); }
    private bool used;

    internal static EcuIdentity IdentifyForBackup(Func<IBenchFlashTransport> factory, string mode)
        => InspectEcu(factory, mode, link =>
        {
            var identity = ReadWorkflow.Identify(link);
            ReadWorkflow.CheckEngineStopped(link);
            return identity;
        });

    internal static FlashEcuState CheckRecoveryState(Func<IBenchFlashTransport> factory, string mode)
        => InspectEcu(factory, mode, link => ProbeRecovery(link.Exchange));

    private static T InspectEcu<T>(Func<IBenchFlashTransport> factory, string mode, Func<IBenchFlashTransport, T> inspect)
    {
        var link = factory();
        T result;
        using (link)
        {
            if (link.Mode != mode) throw new TransportException("Transport/evidence mode mismatch.");
            link.Initialize();
            result = inspect(link);
        }
        if (link.CloseWarning is string warning) throw new TransportException("Adapter cleanup failed: " + warning);
        return result;
    }

    private static void Unlock(byte session, Func<byte[], byte[]> exchange)
    {
        if (!exchange([0x10, session]).AsSpan().SequenceEqual(new byte[] { 0x50, session }))
            throw new TransportException("Unexpected diagnostic session acknowledgement.");
        var seed = exchange([0x27, 1]);
        if (seed.Length != 5 || seed[0] != 0x67 || seed[1] != 1)
            throw new TransportException("Invalid three-byte security seed.");
        if (seed.AsSpan(2).IndexOfAnyExcept((byte)0) >= 0 &&
            !exchange([0x27, 2, .. SecurityAccess.SeedKey(seed.AsSpan(2))]).AsSpan().SequenceEqual(new byte[] { 0x67, 2 }))
            throw new TransportException("Unexpected security access acknowledgement.");
    }

    private static FlashEcuState ProbeRecovery(Func<byte[], byte[]> exchange, Action<byte>? unsupported = null)
    {
        foreach (var request in new byte[][] { [9, 2], [9, 4], [1, 12] })
        {
            var reply = Probe(request);
            if (reply is null) continue;
            bool normal = request[0] == 1
                ? reply.Length == 4 && reply[0] == 0x41 && reply[1] == request[1]
                : reply.Length > 3 && reply[0] == 0x49 && reply[1] == request[1] && reply[2] == 1 &&
                  reply.AsSpan(3).TrimEnd((byte)0).IndexOfAnyInRange((byte)33, (byte)126) >= 0 &&
                  reply.AsSpan(3).TrimEnd((byte)0).IndexOfAnyExceptInRange((byte)32, (byte)126) < 0;
            if (normal) return FlashEcuState.Normal;
            throw new TransportException("Unexpected reply to an ECU identification probe.");
        }
        Unlock(0x87, exchange);
        // Gen 1 read-memory probe for exactly one byte at address zero.
        var memory = Probe([0x23, 0, 0, 0, 0, 0, 1]);
        if (memory is null) return FlashEcuState.Recovery;
        if (memory.Length == 2 && memory[0] == 0x63) return FlashEcuState.Normal;
        throw new TransportException("Unexpected reply to the Gen 1 recovery probe.");

        byte[]? Probe(byte[] request)
        {
            try { return exchange(request); }
            catch (NegativeResponseException error) when (error.Service == request[0] && error.Code == 0x11)
            {
                unsupported?.Invoke(request[0]);
                return null;
            }
        }
    }
    public BenchFlashResult Execute(BenchFlashPlan plan, Func<IBenchFlashTransport> factory, string vault,
        IProgress<BenchFlashProgress>? progress = null, CancellationToken cancellation = default, string evidenceMode = "hardware",
        bool singleReadback = false, bool skipReadback = false)
    {
        if (singleReadback && skipReadback) throw new ArgumentException("Choose one readback policy.");
        return ExecuteCore(plan, null, factory, vault, progress, cancellation, evidenceMode, skipReadback ? 0 : singleReadback ? 1 : 2);
    }

    public BenchFlashResult Recover(BenchRecoveryPlan recovery, bool samePhysicalEcuConfirmed, Func<IBenchFlashTransport> factory,
        string vault, IProgress<BenchFlashProgress>? progress = null, CancellationToken cancellation = default,
        string evidenceMode = "hardware")
    {
        if (!samePhysicalEcuConfirmed)
            throw new InvalidDataException("Confirm the same physical ECU and its original backup before recovery.");
        return ExecuteCore(recovery.Stock, recovery, factory, vault, progress, cancellation, evidenceMode, 2);
    }

    private BenchFlashResult ExecuteCore(BenchFlashPlan plan, BenchRecoveryPlan? recovery, Func<IBenchFlashTransport> factory, string vault,
        IProgress<BenchFlashProgress>? progress, CancellationToken cancellation, string evidenceMode, int readbackPasses)
    {
        bool singleReadback = readbackPasses == 1;
        if (used) throw new InvalidOperationException("Create a new workflow for every attempt. Resume and retransmission are unavailable.");
        used = true;
        if (evidenceMode is not ("hardware" or "simulator")) throw new ArgumentException("Invalid evidence mode.");
        if (!NativeWriterProfiles.TryGetValue(plan.Profile.Calibration, out ushort advertisedLimit) ||
            evidenceMode == "hardware" && plan.BackupMode != "hardware")
            throw new InvalidDataException("This calibration has not passed native writer qualification for hardware access.");
        IBenchFlashTransport? link = null;
        OperationJournal? journal = null;
        bool resetAcknowledged = false;
        void Update(BenchFlashStage stage, string message, double fraction, bool canCancel)
        { Stage = stage; progress?.Report(new(stage, message, fraction, AcknowledgedBytes, canCancel)); }
        void CloseLink()
        {
            var old = link; link = null; old?.Dispose();
            if (old?.CloseWarning is string warning) throw new TransportException("Adapter cleanup failed: " + warning);
        }
        void Log(string name, Dictionary<string, object?> fields) => journal!.Append(name, fields);
        byte[] Send(byte[] request, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            // Flush intent before dispatch. Firmware bytes are never written to diagnostics.
            Log("request_issued", new() { ["stage"] = Stage.ToString(), ["service"] = request[0], ["bytes"] = request.Length,
                ["sha256"] = RomInspector.Hash(request), ["securityHex"] = request[0] == 0x27 ? Convert.ToHexString(request) : null });
            var elapsed = Stopwatch.StartNew();
            var response = link!.Exchange(request);
            Log("response_received", new() { ["stage"] = Stage.ToString(), ["service"] = request[0], ["bytes"] = response.Length,
                ["elapsedMs"] = elapsed.Elapsed.TotalMilliseconds,
                ["sha256"] = RomInspector.Hash(response), ["responseHex"] = request[0] == 0x23 || request[0] == 0x36 && response.Length > 1 || response.Length > 16
                    ? Convert.ToHexString(response.AsSpan(0, Math.Min(1, response.Length))) + " [payload omitted]" : Convert.ToHexString(response) });
            return response;
        }
        void Exact(byte[] request, byte[] expected, CancellationToken cancel = default)
        { if (!Send(request, cancel).AsSpan().SequenceEqual(expected)) throw new TransportException("Unexpected acknowledgement in " + Stage + ". No request was repeated."); }
        byte[] PreflightExchange(byte[] request) => Send(request, cancellation);
        int CheckVoltage()
        {
            int mv = link!.BatteryMillivolts;
            if (mv is < 12000 or > 15000)
                throw new TransportException($"Bench supply is outside the approved range (12.0–15.0 V). Measured {mv / 1000.0:0.00} V.");
            return mv;
        }
        EcuIdentity CheckLiveIdentity()
        {
            cancellation.ThrowIfCancellationRequested();
            var actual = ReadWorkflow.Identify(link!);
            if (!plan.MatchesLive(actual)) throw new TransportException("The connected ECU does not match the saved baseline identity.");
            ReadWorkflow.CheckEngineStopped(link!);
            Log("preflight_identity", new() { ["calibration"] = actual.Calibration, ["vehicleMatched"] = true, ["batteryMillivolts"] = CheckVoltage() });
            return actual;
        }
        byte[] ReadRange(int address, int count)
        {
            var request = ReadProtocol.For(plan.Identity).ReadRequest(address, count);
            var reply = Send(request, cancellation);
            if (reply.Length != count + 1 || reply[0] != 0x63)
                throw new TransportException("Short or malformed preflight ROM read.");
            return reply[1..];
        }
        void CheckBootOnly()
        {
            CheckVoltage();
            if (ProbeRecovery(PreflightExchange, service => Log("boot_probe_rejected", new() { ["service"] = service, ["nrc"] = 0x11 }))
                != FlashEcuState.Recovery)
                throw new TransportException("The ECU is responding in normal mode. Nothing was erased.");
        }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var created = store.CreateOperation(vault, evidenceMode, recovery is null ? "local_bench_flash" : "local_bench_recovery");
            RunFolder = created.Folder; journal = created.Journal;
            DurableStorage.NewJson(store.FileSystem, Path.Combine(RunFolder, "plan.json"), plan.Summary(readbackPasses));
            DurableStorage.NewFile(store.FileSystem, Path.Combine(RunFolder, "intended-image.bin"), plan.Image);
            DurableStorage.NewFile(store.FileSystem, Path.Combine(RunFolder, "original-baseline.bin"), plan.Baseline);
            foreach (string name in new[] { "plan.json", "intended-image.bin", "original-baseline.bin" })
                store.FileSystem.MarkReadOnly(Path.Combine(RunFolder, name));
            Update(BenchFlashStage.Preflight, recovery is null ? "Identify ECU, check RPM and supply, and compare retained boot bytes" :
                "Check supply and confirm the retained boot server is answering", 0, true);
            link = factory();
            if (link.Mode != evidenceMode) throw new TransportException("Transport/evidence mode mismatch.");
            link.Initialize(); Log("adapter_opened", new() { ["adapter"] = link.Adapter, ["mode"] = link.Mode });
            if (recovery is null)
            {
                CheckLiveIdentity(); Unlock(0x87, PreflightExchange);
                var calibrationBytes = ReadRange(plan.Profile.IdentityOffset, plan.FullCalibration.Length);
                if (!calibrationBytes.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(plan.FullCalibration)))
                    throw new TransportException("Live ROM does not contain the exact expected calibration ID. Nothing was erased.");
                for (int at = 0; at < BenchFlashPlan.Start; at += BenchFlashPlan.Chunk)
                {
                    int count = Math.Min(BenchFlashPlan.Chunk, BenchFlashPlan.Start - at);
                    if (!ReadRange(at, count).AsSpan().SequenceEqual(plan.Baseline.AsSpan(at, count)))
                        throw new TransportException("Live boot or ECU-specific data differs from the baseline. Nothing was erased.");
                    Update(Stage, "Comparing retained boot and ECU-specific data", .08 * (at + count) / BenchFlashPlan.Start, true);
                }
                Log("retained_boot_verified", new() { ["bytes"] = BenchFlashPlan.Start, ["sha256"] = plan.BootSha256 });
            }
            else CheckBootOnly();
            Log("recovery_binding", new() { ["calibration"] = plan.FullCalibration, ["backupSha256"] = plan.BackupSha256,
                ["bootSha256"] = plan.BootSha256, ["vehicleIdentitySha256"] = plan.VehicleIdentityHash,
                ["priorJournalSha256"] = recovery?.PriorJournalSha256 });
            CheckVoltage();
            Update(BenchFlashStage.ProgrammingSession, "Enter programming session", .08, true);
            Unlock(0x85, PreflightExchange);
            cancellation.ThrowIfCancellationRequested();
            // No cancellation or UI-driven adapter disposal through erase/program/reset.
            Update(BenchFlashStage.Erasing, "Erasing application; keep power and USB connected", .1, false);
            cancellation.ThrowIfCancellationRequested();
            Log("destructive_boundary", new() { ["start"] = BenchFlashPlan.Start, ["bytes"] = BenchFlashPlan.Length });
            DestructiveRequestIssued = true;
            Exact([0xB1, 0, 0xB2, 0], [0xF1, 0, 0xB2]);
            Update(BenchFlashStage.Download, "Prepare complete application transfer", .12, false);
            Exact(Convert.FromHexString("3400008000000F8000"), [0x74, (byte)(advertisedLimit >> 8), (byte)advertisedLimit]);
            for (int at = BenchFlashPlan.Start; at < RomInspector.RomSize; at += BenchFlashPlan.Chunk)
            {
                Update(BenchFlashStage.Transfer, $"Writing block {AcknowledgedBytes / BenchFlashPlan.Chunk + 1} of 256", .12 + .66 * AcknowledgedBytes / BenchFlashPlan.Length, false);
                Exact([0x36, .. plan.Image.AsSpan(at, BenchFlashPlan.Chunk)], [0x76]);
                AcknowledgedBytes += BenchFlashPlan.Chunk;
                Log("block_acknowledged", new() { ["address"] = at, ["bytes"] = BenchFlashPlan.Chunk, ["total"] = AcknowledgedBytes });
            }
            Update(BenchFlashStage.Finalizing, "Finalize application transfer", .79, false); Exact([0x37], [0x77]);
            Update(BenchFlashStage.Resetting, "Reset ECU after acknowledged transfer", .8, false); Exact([0x11, 1], [0x51]);
            resetAcknowledged = true; CloseLink();
            Update(BenchFlashStage.Reconnecting, "Reconnect to the application and confirm ECU identity", .8, false);
            resetWait(TimeSpan.FromSeconds(3));
            link = factory();
            if (link.Mode != evidenceMode) throw new TransportException("Transport/evidence mode mismatch.");
            link.Initialize(); Log("adapter_reopened", new() { ["adapter"] = link.Adapter, ["mode"] = link.Mode });
            var verifiedIdentity = CheckLiveIdentity();
            if (readbackPasses == 0)
            {
                CloseLink();
                DurableStorage.NewJson(store.FileSystem, Path.Combine(RunFolder, "unverified-result.json"),
                    new { plan.ImageSha256, readbackPasses, acknowledgedBytes = AcknowledgedBytes, identityConfirmed = true, verified = false });
                Log("completed", new() { ["sha256"] = plan.ImageSha256, ["bytes"] = AcknowledgedBytes,
                    ["readbackPasses"] = 0, ["verified"] = false });
                Update(BenchFlashStage.WrittenUnverified, "Write acknowledged · ECU reconnected · readback skipped", 1, false);
                return new(RunFolder, null, plan.ImageSha256);
            }
            Update(BenchFlashStage.Readback, singleReadback ? "Compare one complete ROM readback with the intended image" : "Verify two complete ROM readbacks", .8, true);
            var readProgress = new InlineProgress<ReadProgress>(p => Update(BenchFlashStage.Readback,
                p.Pass > 0 ? $"{p.Message} · {p.BytesInPass:N0} / {RomInspector.RomSize:N0} readback bytes" : p.Message,
                Math.Min(.99, .8 + .19 * p.Fraction), true));
            var readWorkflow = new ReadWorkflow(store, plan.Inspector);
            string readbackVault = Path.Combine(RunFolder, "readback");
            string readback;
            if (singleReadback)
            {
                readback = readWorkflow.VerifyKnownImage(link, verifiedIdentity, readbackVault, plan.Image, readProgress, cancellation);
            }
            else
            {
                var backup = readWorkflow.ReadBackup(link, verifiedIdentity, readbackVault, readProgress, cancellation);
                var bytes = store.Load(backup.Path, link.Mode).Data;
                if (!bytes.AsSpan().SequenceEqual(plan.Image) || !backup.Inspection.ProfileValid || backup.Inspection.ChecksumValid != true)
                    throw new TransportException("Full readback or image integrity differs from the intended image.");
                readback = backup.Path;
            }
            CloseLink();
            DurableStorage.NewJson(store.FileSystem, Path.Combine(RunFolder, "verified-result.json"), new { plan.ImageSha256, readback, completeBytes = plan.Image.Length,
                readbackPasses, twoMatchingPasses = !singleReadback, bootPreserved = true });
            Log("completed", new() { ["sha256"] = plan.ImageSha256, ["bytes"] = plan.Image.Length, ["readbackPasses"] = readbackPasses, ["twoMatchingReadbacks"] = !singleReadback });
            Update(BenchFlashStage.Complete, singleReadback ? "Write verified against one complete readback" : "Write verified against two complete readbacks", 1, false);
            return new(RunFolder, readback, plan.ImageSha256);
        }
        catch (Exception error)
        {
            var failed = DestructiveRequestIssued ? (resetAcknowledged ? BenchFlashStage.VerificationRequired : BenchFlashStage.RecoveryRequired)
                : error is OperationCanceledException ? BenchFlashStage.Stopped : BenchFlashStage.Failed;
            Update(failed, error.Message, 0, false);
            journal?.RecordStopped(new() { ["stage"] = failed.ToString(), ["error"] = error.Message,
                ["acknowledgedBytes"] = AcknowledgedBytes, ["requestRetried"] = false, ["destructiveRequestIssued"] = DestructiveRequestIssued });
            throw;
        }
        finally
        {
            try { link?.Dispose(); }
            finally { journal?.Dispose(); }
        }
    }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
