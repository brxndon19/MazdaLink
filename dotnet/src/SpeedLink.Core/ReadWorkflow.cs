using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace SpeedLink.Core;

public static class SecurityAccess
{
    public static byte[] SeedKey(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 3)
            throw new InvalidDataException("The read-access seed must contain exactly three bytes.");
        uint state = 0xC541A9;
        foreach (var value in seed.ToArray().Concat("MazdA"u8.ToArray()))
        {
            int current = value;
            for (int bit = 0; bit < 8; bit++)
            {
                state = ((state >> 1) ^ (((state ^ (uint)current) & 1) != 0 ? 0x909028u : 0u)) & 0xFFFFFF;
                current >>= 1;
            }
        }
        return [(byte)((state >> 4) & 255), (byte)(((state >> 8) & 240) | ((state >> 20) & 15)), (byte)(((state << 4) & 240) | ((state >> 16) & 15))];
    }
}
public enum BackupReadStage { Preparing, Reading, Verifying, Complete }
public sealed record ReadProgress(double Fraction, string Message)
{
    public BackupReadStage Stage { get; init; } = BackupReadStage.Reading;
    public int Pass { get; init; }
    public int BytesInPass { get; init; }
}
/// <summary>Retains durable read progress even when its display callback is still queued.</summary>
public sealed class ReadProgressCapture(IProgress<ReadProgress> display) : IProgress<ReadProgress>
{
    private ReadProgress? latest;
    public ReadProgress? Latest => Volatile.Read(ref latest);
    public void Report(ReadProgress value)
    {
        Volatile.Write(ref latest, value);
        display.Report(value);
    }
}
public sealed record BackupResult(string Path, RomInspection Inspection);

public sealed class ReadWorkflow
{
    private readonly BackupStore store;
    private readonly RomInspector inspector;
    public ReadWorkflow(BackupStore? store = null, RomInspector? inspector = null)
    {
        this.store = store ?? new();
        this.inspector = inspector ?? new();
    }
    public static EcuIdentity Identify(IReadTransport transport)
    {
        string? vin = null, chassisId = null, calibration = null, vinStatus = null, calibrationStatus = null;
        foreach (var code in new byte[] { 0x02, 0x04 })
        {
            try
            {
                var response = transport.Exchange([0x09, code]);
                if (response.Length < 3 || response[0] != 0x49 || response[1] != code || response[2] != 1)
                    throw new TransportException("Unrecognized identification response format.");
                var text = response.AsSpan(3).TrimEnd((byte)0);
                if (code == 0x02)
                    while (!text.IsEmpty && text[^1] is 0 or 32) text = text[..^1];
                if (text.IsEmpty || text.ToArray().Any(c => c < 32 || c > 126))
                    throw new TransportException("Invalid ECU identity text.");
                var value = Encoding.ASCII.GetString(text);
                if (code == 0x02)
                {
                    if (value.Length == 17 && value.All(c => "ABCDEFGHJKLMNPRSTUVWXYZ0123456789".Contains(c)))
                        vin = value;
                    else if (Regex.IsMatch(value, @"\A[A-Z][A-Z0-9]{2,5}-?[0-9]{6}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
                    {
                        chassisId = value;
                        vinStatus = "ECU reports a chassis ID instead of a 17-character VIN.";
                    }
                    else
                        throw new TransportException("Invalid VIN response.");
                    transport.Diagnostics?.RegisterSecret(value);
                }
                else
                    calibration = value;
            }
            catch (NegativeResponseException error) { if (code == 0x02) vinStatus = error.Message; else calibrationStatus = error.Message; }
        }
        return new(transport.Mode, transport.Adapter, vin, calibration, vinStatus, calibrationStatus) { ChassisId = chassisId };
    }
    public static void CheckEngineStopped(IReadTransport transport)
    {
        var response = transport.Exchange([0x01, 0x0C]);
        if (response.Length != 4 || response[0] != 0x41 || response[1] != 0x0C)
            throw new TransportException("Could not establish engine RPM. Read was not started.");
        var rpm = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2)) / 4.0;
        if (rpm != 0)
            throw new TransportException($"Engine is running ({rpm:g} RPM). Switch it off before reading.");
    }
    /// <summary>Enters the read session and unlocks it once. Returns true when the ECU required a security key.</summary>
    public static bool OpenReadSession(IReadTransport transport, ReadProtocol protocol, CancellationToken cancellation = default)
    {
        var session = transport.Exchange([0x10, protocol.Session]);
        if ((protocol.Family == ReadFamily.Gen2 ? session.Length != 6 : session.Length < 2) || session[0] != 0x50 || session[1] != protocol.Session)
            throw new TransportException($"ECU did not acknowledge read session {protocol.Session:X2}.");
        cancellation.ThrowIfCancellationRequested();
        var seed = transport.Exchange([0x27, protocol.SeedLevel]);
        if (seed.Length != 5 || seed[0] != 0x67 || seed[1] != protocol.SeedLevel)
            throw new TransportException("Unexpected security seed format.");
        if (seed.AsSpan(2).SequenceEqual(new byte[3]))
            return false;
        cancellation.ThrowIfCancellationRequested();
        var response = transport.Exchange([0x27, protocol.KeyLevel, .. SecurityAccess.SeedKey(seed.AsSpan(2))]);
        if (!response.AsSpan().SequenceEqual(new byte[] { 0x67, protocol.KeyLevel }))
            throw new TransportException("Read-session key was not acknowledged. No retry was attempted.");
        return true;
    }
    public BackupResult ReadBackup(IReadTransport transport, EcuIdentity expected, string vault, IProgress<ReadProgress>? progress = null, CancellationToken cancellation = default, ReadFamily unknownFamily = ReadFamily.Gen1, int? maximumBlock = null)
    {
        if (maximumBlock is < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumBlock));
        return Capture(transport, expected, vault, progress, cancellation, unknownFamily, null, maximumBlock);
    }

    public string VerifyKnownImage(IReadTransport transport, EcuIdentity expected, string vault, ReadOnlySpan<byte> expectedImage,
        IProgress<ReadProgress>? progress = null, CancellationToken cancellation = default)
    {
        if (expectedImage.Length != RomInspector.RomSize)
            throw new ArgumentException("Verification requires the complete expected ROM.", nameof(expectedImage));
        return Capture(transport, expected, vault, progress, cancellation, ReadFamily.Gen1, expectedImage.ToArray(), null).Path;
    }

    private BackupResult Capture(IReadTransport transport, EcuIdentity expected, string vault, IProgress<ReadProgress>? progress,
        CancellationToken cancellation, ReadFamily unknownFamily, byte[]? expectedImage, int? maximumBlock)
    {
        int passes = expectedImage is null ? 2 : 1;
        if (transport.Mode == "hardware")
        {
            if (transport.ReadAttempted)
                throw new TransportException("Reconnect and identify before another read.");
            transport.ReadAttempted = true;
        }
        var (folder, journal) = store.CreateOperation(vault, transport.Mode, expectedImage is null ? "read_backup" : "verify_known_image");
        transport.Diagnostics?.AttachJournal(journal.Path);
        transport.Diagnostics?.Record("read.started", new { chunkBytes = transport.ReadChunkSize });
        BackupResult result;
        using (journal)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                string action = expectedImage is null ? "backup" : "readback";
                progress?.Report(new(0, $"Preparing {action} · checking ECU and read access…") { Stage = BackupReadStage.Preparing });
                var identity = Identify(transport);
                if (!identity.MatchesVehicle(expected))
                    throw new TransportException("ECU identity changed. Reconnect and identify again.");
                CheckEngineStopped(transport);
                journal.Append("identity", new()
                {
                    ["identity"] = identity.ToManifest()
                });
                cancellation.ThrowIfCancellationRequested();
                var protocol = ReadProtocol.For(identity, unknownFamily);
                if (maximumBlock is { } limit && limit < protocol.MaximumBlock)
                    protocol = protocol with { MaximumBlock = limit };
                journal.Append("read_protocol", new() { ["family"] = protocol.Family.ToString(), ["session"] = protocol.Session, ["securityLevel"] = protocol.SeedLevel, ["maximumBlock"] = protocol.MaximumBlock });
                transport.Diagnostics?.Record("read.protocol", new { family = protocol.Family.ToString(), protocol.Session, protocol.SeedLevel, protocol.MaximumBlock });
                OpenReadSession(transport, protocol, cancellation);
                journal.Append("read_session_ready");
                var hashes = new List<string>();
                byte[] data = [];
                int chunk = transport.ReadChunkSize;
                if (chunk is < 1 or > 3968)
                    throw new TransportException("Unsupported read chunk size.");
                chunk = Math.Min(chunk, protocol.MaximumBlock);
                for (int pass = 1; pass <= passes; pass++)
                {
                    transport.Diagnostics?.Record("read.pass_started", new { pass });
                    string passMessage = $"Reading pass {pass} of {passes}";
                    progress?.Report(new((double)(pass - 1) / passes, passMessage) { Pass = pass });
                    data = new byte[RomInspector.RomSize];
                    using var file = store.FileSystem.CreateNew(System.IO.Path.Combine(folder, $"pass{pass}.read"));
                    for (int address = 0; address < data.Length; address += chunk)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int count = Math.Min(chunk, data.Length - address);
                        byte[] request = protocol.ReadRequest(address, count);
                        journal.Append("read_issued", new()
                        {
                            ["passNumber"] = pass,
                            ["address"] = address,
                            ["bytes"] = count
                        });
                        var response = transport.Exchange(request);
                        if (response.Length != count + 1 || response[0] != 0x63)
                            throw new TransportException($"Short or malformed read at 0x{address:X6}; expected {count} bytes.");
                        var block = response.AsSpan(1).ToArray();
                        DurableStorage.Write(file, block);
                        block.CopyTo(data, address);
                        journal.Append("read_saved", new()
                        {
                            ["passNumber"] = pass,
                            ["address"] = address,
                            ["bytes"] = count,
                            ["sha256"] = RomInspector.Hash(block)
                        });
                        if (expectedImage is not null && !block.AsSpan().SequenceEqual(expectedImage.AsSpan(address, count)))
                            throw new InvalidDataException($"Readback differs from the intended image in the block at 0x{address:X6}.");
                        progress?.Report(new(((pass - 1) * RomInspector.RomSize + address + count) / ((double)passes * RomInspector.RomSize), passMessage) { Pass = pass, BytesInPass = address + count });
                    }
                    hashes.Add(RomInspector.Hash(data));
                    journal.Append("read_pass_complete", new()
                    {
                        ["passNumber"] = pass,
                        ["sha256"] = hashes[^1]
                    });
                }
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(new(1, $"Verifying and saving {action}…") { Stage = BackupReadStage.Verifying, Pass = passes, BytesInPass = RomInspector.RomSize });
                if (expectedImage is null && hashes[0] != hashes[1])
                    throw new TransportException("The two ROM reads differ. Both captures were retained; no verified backup was created.");
                var inspection = inspector.Inspect(data);
                string path;
                if (expectedImage is null)
                {
                    path = store.Seal(folder, data, identity with { ReadProtocolName = protocol.Family.ToString() }, inspection, hashes[0], hashes[1], transport.Mode);
                }
                else
                {
                    path = Path.Combine(folder, "pass1.read");
                    if (!inspection.ProfileValid || inspection.ChecksumValid != true ||
                        !store.FileSystem.Read(path).AsSpan().SequenceEqual(expectedImage))
                        throw new InvalidDataException("The saved readback differs from the intended image or failed its integrity check.");
                    store.FileSystem.MarkReadOnly(path);
                }
                journal.Append("completed", new()
                {
                    [expectedImage is null ? "rom" : "readback"] = path,
                    ["sha256"] = hashes[0],
                    ["profileValid"] = inspection.ProfileValid
                });
                result = new(path, inspection);
            }
            catch (Exception error)
            {
                journal.RecordStopped(new()
                {
                    ["reason"] = error.Message,
                    ["noAutomaticRetry"] = true
                });
                throw;
            }
        }
        string completed = expectedImage is null ? "Backup verified · two matching reads" : "Complete readback matches the intended image";
        progress?.Report(new(1, completed) { Stage = BackupReadStage.Complete, Pass = passes, BytesInPass = RomInspector.RomSize });
        return result;
    }
}
