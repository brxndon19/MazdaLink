using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SpeedLink.Core;
namespace SpeedLink.Tests;

internal static class Fixture
{
    public static byte[] Image(RomProfile profile)
    {
        var data = new byte[RomInspector.RomSize];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 19 + (i >> 8) * 7) & 255);
        "SYNTHETIC DATA - NOT ECU FIRMWARE"u8.CopyTo(data.AsSpan(0x100));
        data[0x8002] = 0x5A;
        data[0x8003] = 0xA5;
        data[^2] = 0xA5;
        data[^1] = 0x5A;
        Encoding.ASCII.GetBytes(profile.Identity).CopyTo(data, profile.IdentityOffset);
        foreach (var guard in profile.LayoutWords)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(guard.Address, 4), guard.Value);
        foreach (var marker in profile.Markers ?? []) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(marker.Address, 2), marker.Value);
        // Synthetic images need a balanced sum; this fixture location is never used by production code.
        int correction = profile.CorrectionAddress >= 0 ? profile.CorrectionAddress : 0xBFFF0;
        data.AsSpan(correction, 4).Clear();
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(correction, 4), unchecked(RomInspector.ChecksumTarget - RomInspector.Sum(data, profile)));
        return data;
    }
    public static IReadOnlyList<RomProfile> Profiles
    {
        get;
    } = RomInspector.KnownProfiles.Select(p =>
    {
        var data = Image(p);
        return p with
        {
            BootSha256 = RomInspector.BootHash(data, p),
            LoopSha256 = p.RoutineRanges is not null ? RomInspector.Hash(p.RoutineRanges.SelectMany(r => data.Skip(r.Address).Take(r.Length)).ToArray()) :
                p.LoopAddress is int at ? RomInspector.Hash(data.AsSpan(at, p.LoopSize!.Value)) : null
        };
    }).ToArray();
    public static RomInspector Inspector() => new(Profiles);
    public static TuningCodeGuard TuningGuard { get; } = new(Profiles.Select(p =>
    {
        var definition = TuningDefinition.Known.Single(d => d.Calibration == p.Calibration);
        return new TuningCodeSignature(p.Calibration, p.ChecksumStart, TuningCodeGuard.Hash(Image(p), definition, p));
    }).ToArray());
    public static byte[] Rom => Image(Profiles[0]);
    public static BackupStore Store(TestFileSystem? files = null) => new(files ?? new());
    public static ReadWorkflow Reader(TestFileSystem? files = null) => new(Store(files), Inspector());
    public static string Multiline(byte[] data) => $"{data.Length:X3}\r" + string.Join('\r', data.Chunk(7).Select((block, index) => $"{index % 16:X}:{Convert.ToHexString(block)}")) + "\r>";
    public static byte[] Hex(string value) => Convert.FromHexString(value);
}
internal sealed class TempFolder : IDisposable
{
    private static readonly string Parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpeedLink.CSharp.Tests"));
    public string Path { get; } = System.IO.Path.Combine(Parent, Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(Parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing cleanup outside test temporary root.");
        if (!Directory.Exists(resolved))
            return;
        foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(resolved, recursive: true);
    }
}
internal sealed class TestFileSystem : IBackupFileSystem
{
    private readonly BackupFileSystem real = new();
    public bool Durable
    {
        get; init;
    }
    public Func<string, byte[], bool>? Predicate
    {
        get; init;
    }
    public string? Fault
    {
        get; init;
    }
    public bool Fired
    {
        get; private set;
    }
    public string? FailedPath
    {
        get; private set;
    }
    public int JournalWritesAfterFailure
    {
        get; private set;
    }
    private bool Hit(string path, byte[] data, string phase)
    {
        if (!Fired && Fault == phase && Predicate?.Invoke(path, data) == true)
        {
            Fired = true;
            FailedPath = path;
            return true;
        }
        return false;
    }
    private sealed class Handle(TestFileSystem owner, string path, IDurableFile real) : IDurableFile
    {
        private byte[] last = [];
        public int Write(byte[] data)
        {
            if (owner.Fired && owner.FailedPath == path && System.IO.Path.GetFileName(path) == "journal.jsonl")
                owner.JournalWritesAfterFailure++;
            last = data;
            if (owner.Hit(path, data, "short"))
            {
                var count = Math.Max(1, data.Length / 2);
                real.Write(data[..count]);
                return count;
            }
            if (owner.Hit(path, data, "disk_full"))
                throw new IOException("Injected disk full.");
            return real.Write(data);
        }
        public void Flush()
        {
            if (owner.Hit(path, last, "flush"))
                throw new IOException("Injected flush failure.");
            real.Flush();
        }
        public void Sync()
        {
            if (owner.Hit(path, last, "fsync"))
                throw new IOException("Injected fsync failure.");
            if (owner.Durable)
                real.Sync();
        }
        public void Dispose() => real.Dispose();
    }
    public IDurableFile CreateNew(string path)
    {
        if (Hit(path, [], "open"))
            throw new UnauthorizedAccessException("Injected journal open denial.");
        return new Handle(this, path, real.CreateNew(path));
    }
    public byte[] Read(string path) => real.Read(path);
    public bool Exists(string path) => real.Exists(path);
    public void Delete(string path)
    {
        if (Hit(path, [], "delete"))
            throw new UnauthorizedAccessException("Injected completion marker denial.");
        real.Delete(path);
    }
    public void MarkReadOnly(string path) => real.MarkReadOnly(path);
    public void CreateDirectory(string path) => real.CreateDirectory(path);
    public static Func<string, byte[], bool> FileIs(string name) => (path, _) => System.IO.Path.GetFileName(path) == name;
    public static Func<string, byte[], bool> EventIs(string name, string? stage = null, int? block = null) => (path, data) =>
    {
        if (System.IO.Path.GetFileName(path) != "journal.jsonl")
            return false;
        var row = JsonNode.Parse(data)!;
        return row["event"]!.GetValue<string>() == name && (stage is null || row["details"]?["stage"]?.GetValue<string>() == stage)
            && (block is null || row["details"]?["block"]?.GetValue<int>() == block);
    };
}

internal class TestEcu(byte[] rom) : IReadTransport
{
    public ReadFamily? FamilyOverride { get; set; }
    public string? ReportedCalibration { get; set; }
    private ReadProtocol Protocol => ReadProtocol.For(new(Mode, Adapter, null, ReportedCalibration ?? new RomInspector().ProfileFor(Memory)?.Calibration), FamilyOverride ?? ReadFamily.Gen1);
    public byte[] Memory { get; } = rom.ToArray();
    public string Mode => "simulator";
    public string Adapter => "OFFLINE TEST MODEL — no device I/O";
    public int ReadChunkSize { get; set; } = 3968;
    public bool ReadAttempted
    {
        get; set;
    }
    public bool ReadSession
    {
        get; private set;
    }
    public bool ReadUnlocked
    {
        get; private set;
    }
    public bool Closed
    {
        get; private set;
    }
    public byte[] Seed { get; set; } = [0x00, 0x01, 0x77];
    public int Cursor { get; private set; } = 0x8000;
    private int state;
    public List<byte[]> Requests { get; } = [];
    public List<byte[]> Programs { get; } = [];
    public Func<byte[], byte[], byte[]>? ModifyResponse
    {
        get; set;
    }
    public virtual byte[] Exchange(byte[] request)
    {
        if (Closed)
            throw new TransportException("Test model is disconnected.");
        Requests.Add(request.ToArray());
        var hex = Convert.ToHexString(request);
        byte[] response;
        if (hex == "0902")
            response = [0x49, 0x02, 0x01, .. "JM1GG12L061000001"u8.ToArray()];
        else if (hex == "0904")
            response = [0x49, 0x04, 0x01, .. Encoding.ASCII.GetBytes(ReportedCalibration ?? new RomInspector().ProfileFor(Memory)?.Calibration ?? "UNKNOWN")];
        else if (hex == "010C")
            response = [0x41, 0x0C, 0, 0];
        else if (request.AsSpan().SequenceEqual(new byte[] { 0x10, Protocol.Session }))
        {
            ReadSession = true;
            ReadUnlocked = false;
            response = Protocol.Family == ReadFamily.Gen2 ? [0x50, 3, 0, 50, 1, 244] : [0x50, Protocol.Session];
        }
        else if (request.AsSpan().SequenceEqual(new byte[] { 0x27, Protocol.SeedLevel }))
            response = [0x67, Protocol.SeedLevel, .. Seed];
        else if (request.AsSpan().SequenceEqual(new byte[] { 0x27, Protocol.KeyLevel }.Concat(SecurityAccess.SeedKey(Seed)).ToArray()))
        {
            if (!ReadSession)
                throw new TransportException("Read session required.");
            ReadUnlocked = true;
            response = [0x67, Protocol.KeyLevel];
        }
        else if (request.Length == (Protocol.Family == ReadFamily.Gen2 ? 8 : 7) && request[0] == 0x23)
        {
            if (!ReadUnlocked && Seed.Any(x => x != 0))
                throw new NegativeResponseException(0x23, 0x33);
            int start = Protocol.Family == ReadFamily.Gen2 ? 2 : 1;
            int at = BinaryPrimitives.ReadInt32BigEndian(request.AsSpan(start, 4));
            int count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(start + 4, 2));
            response = [0x63, .. Memory.AsSpan(at, count).ToArray()];
        }
        else
            throw new TransportException("Unsupported offline request.");
        return ModifyResponse?.Invoke(request, response) ?? response;
    }
    public byte[] Program(byte[] request)
    {
        Programs.Add(request.ToArray());
        var hex = Convert.ToHexString(request);
        if (hex == "1085")
        {
            state = 1;
            Cursor = 0x8000;
            return [0x50, 0x85];
        }
        if (hex == "2701" && state == 1)
            return [0x67, 0x01, .. Seed];
        if (request.AsSpan().SequenceEqual(new byte[] { 0x27, 0x02 }.Concat(SecurityAccess.SeedKey(Seed)).ToArray()) && state == 1)
        {
            state = 2;
            return [0x67, 0x02];
        }
        if (hex == "B100B200" && state == 2)
        {
            Memory.AsSpan(0x8000).Fill(0xFF);
            state = 3;
            return [0xF1, 0, 0xB2];
        }
        if (hex == "3400008000000F8000" && state == 3)
        {
            state = 4;
            return [0x74, 0x0F, 0xFF];
        }
        if (request[0] == 0x36 && state == 4)
        {
            var payload = request.AsSpan(1);
            if (payload.Length is 0 or > 4094 || Cursor + payload.Length > Memory.Length)
                return [0x7F, 0x36, 0x31];
            payload.CopyTo(Memory.AsSpan(Cursor));
            Cursor += payload.Length;
            if (Cursor == Memory.Length)
                state = 6;
            return [0x76];
        }
        if (hex == "37" && state == 6)
        {
            state = 7;
            return [0x77];
        }
        if (hex == "1101" && state == 7)
        {
            state = 0;
            return [0x51];
        }
        return [0x7F, request[0], 0x24];
    }
    public void Dispose() => Closed = true;
}

internal sealed class TestClock : IMonotonicClock
{
    private long milliseconds;
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(milliseconds++);
    public void Sleep(int value) => milliseconds += value;
}
internal class FakeSerial : ISerialConnection
{
    private readonly Queue<byte> buffer = [];
    private bool payload;
    public List<byte[]> Writes { get; } = [];
    public bool IsOpen { get; private set; } = true;
    public int BaudRate { get; set; } = 115200;
    public int BytesToRead => buffer.Count;
    public bool NoPrompt
    {
        get; set;
    }
    public bool BadReply
    {
        get; set;
    }
    public Func<byte[], byte[]?>? ResponseOverride
    {
        get; set;
    }
    public bool Segmented
    {
        get; init;
    }
    public string IdentityPrefix { get; init; } = "";
    public string Firmware { get; set; } = "vLinker FS v2.3.02";
    public virtual int Write(byte[] data)
    {
        Writes.Add(data.ToArray());
        if (NoPrompt)
            return data.Length;
        var command = Encoding.ASCII.GetString(data).Trim();
        string reply;
        if (payload)
        {
            payload = false;
            var request = Convert.FromHexString(command);
            var custom = ResponseOverride?.Invoke(request);
            if (custom is not null)
                reply = Convert.ToHexString(custom) + "\r>";
            else if (BadReply)
                reply = "CAN ERROR\r>";
            else if (request[0] == 0x23)
            {
                var count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(5));
                reply = Convert.ToHexString(new byte[] { 0x63 }.Concat(Enumerable.Range(0, count).Select(i => (byte)(i % 256))).ToArray()) + "\r>";
            }
            else
                reply = "410C0000\r>";
        }
        else if (command.StartsWith("STPX ", StringComparison.Ordinal))
        {
            payload = true;
            reply = "DATA>";
        }
        else if (command == "ATZ")
            reply = "ELM327 v1.5\r>";
        else if (command == "ATI")
            reply = "ELM327 v2.3\r>";
        else if (command == "VTI")
            reply = Firmware + "\r>";
        else if (command == "STI")
            reply = IdentityPrefix + "STN1170 v4.3.2\r>";
        else if (command.StartsWith("AT", StringComparison.Ordinal) || command.StartsWith("ST", StringComparison.Ordinal))
            reply = "OK\r>";
        else if (command == "0902")
            reply = Fixture.Multiline([0x49, 0x02, 0x01, .. "JM1GG12L061000001"u8.ToArray()]);
        else
            reply = "7F0922\r>";
        foreach (var value in Encoding.ASCII.GetBytes(reply))
            buffer.Enqueue(value);
        return data.Length;
    }
    public int Read(byte[] data, int offset, int count)
    {
        int read = Math.Min(count, buffer.Count);
        for (int i = 0; i < read; i++)
            data[offset + i] = buffer.Dequeue();
        return read;
    }
    public void Flush()
    {
    }
    public void DiscardInput() => buffer.Clear();
    public void Dispose() => IsOpen = false;
}
