using System.Buffers.Binary;
using System.IO;
using System.Text;
using SpeedLink.Core;
namespace SpeedLink.Desktop;

// In-process desktop-test fixture. Cannot load a driver or transmit CAN.
internal sealed class BenchFlashUiPeerState(byte[] baseline, EcuIdentity identity)
{
    internal static (BenchFlashUiPeerState Peer, string Backup, RomInspector Inspector, TuningCodeGuard CodeGuard) Create(string output)
    {
        var profile = RomInspector.KnownProfiles.Single(p => p.Calibration == "L3M6EG000");
        var image = new byte[RomInspector.RomSize];
        "SYNTHETIC UI TEST - NOT ECU FIRMWARE"u8.CopyTo(image.AsSpan(0x100));
        Encoding.ASCII.GetBytes(profile.Identity).CopyTo(image, profile.IdentityOffset);
        foreach (var word in profile.LayoutWords)
            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(word.Address, 4), word.Value);
        image[0x8002] = 0x5A; image[0x8003] = 0xA5; image[^2] = 0xA5; image[^1] = 0x5A;
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(profile.CorrectionAddress, 4),
            unchecked(RomInspector.ChecksumTarget - RomInspector.Sum(image, profile)));
        profile = profile with { BootSha256 = RomInspector.BootHash(image, profile),
            LoopSha256 = profile.LoopAddress is int at ? RomInspector.Hash(image.AsSpan(at, profile.LoopSize!.Value)) : null };
        var inspector = new RomInspector([profile]);
        var definition = TuningDefinition.Known.Single(d => d.Calibration == profile.Calibration);
        var guard = new TuningCodeGuard([new(profile.Calibration, profile.ChecksumStart, TuningCodeGuard.Hash(image, definition, profile))]);
        var identity = new EcuIdentity("simulator", "synthetic flash peer", null, profile.Identity) { ChassisId = "GG3P123456" };
        var peer = new BenchFlashUiPeerState(image, identity);
        using var link = peer.Open();
        link.Initialize();
        string backup = new ReadWorkflow(inspector: inspector).ReadBackup(link, identity, Path.Combine(output, "smoke-backup")).Path;
        return (peer, backup, inspector, guard);
    }

    private readonly byte[] memory = baseline.ToArray();
    private int cursor = 0x8000;
    private bool loseNextTransfer, bootOnly;
    internal void SimulateInterruptedWrite() => loseNextTransfer = true;
    public IBenchFlashTransport Open() => new Peer(this);
    private sealed class Peer(BenchFlashUiPeerState state) : IBenchFlashTransport
    {
        private bool disposed;
        public string Mode => "simulator";
        public string Adapter => "SYNTHETIC desktop peer; no CAN";
        public int ReadChunkSize => 3968;
        public bool ReadAttempted { get; set; }
        public string? CloseWarning => null;
        public int BatteryMillivolts => 13500;
        public void Initialize() { }
        public byte[] Exchange(byte[] p)
        {
            if (disposed) throw new IOException("Closed synthetic peer.");
            if (state.bootOnly && p[0] is 0x09 or 0x01 or 0x23) throw new NegativeResponseException(p[0], 0x11);
            if (p.AsSpan().SequenceEqual(new byte[] { 9, 2 })) return [0x49, 2, 1, .. Encoding.ASCII.GetBytes(state.Identity.VehicleIdentifier!)];
            if (p.AsSpan().SequenceEqual(new byte[] { 9, 4 })) return [0x49, 4, 1, .. Encoding.ASCII.GetBytes(state.Identity.Calibration!)];
            if (p.AsSpan().SequenceEqual(new byte[] { 1, 12 })) return [0x41, 0x0C, 0, 0];
            if (p.Length == 2 && p[0] == 0x10) return [0x50, p[1]];
            if (p.AsSpan().SequenceEqual(new byte[] { 0x27, 1 })) return [0x67, 1, 0, 0, 0];
            if (p[0] == 0x23)
            {
                int at = (int)BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(1)), n = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(5));
                return [0x63, .. state.memory.AsSpan(at, n)];
            }
            if (p.AsSpan().SequenceEqual(new byte[] { 0xB1, 0, 0xB2, 0 })) { state.memory.AsSpan(0x8000).Fill(255); state.cursor = 0x8000; return [0xF1, 0, 0xB2]; }
            if (p.AsSpan().SequenceEqual(Convert.FromHexString("3400008000000F8000"))) return [0x74, 0x0F, 0xFF];
            if (p[0] == 0x36 && p.Length == 3969)
            {
                p.AsSpan(1).CopyTo(state.memory.AsSpan(state.cursor)); state.cursor += 3968;
                if (state.loseNextTransfer)
                {
                    state.loseNextTransfer = false;
                    state.bootOnly = true;
                    throw new TransportException("Synthetic interruption after accepting a transfer block.");
                }
                return [0x76];
            }
            if (p.AsSpan().SequenceEqual(new byte[] { 0x37 }) && state.cursor == 0x100000) return [0x77];
            if (p.AsSpan().SequenceEqual(new byte[] { 0x11, 1 })) { state.bootOnly = false; return [0x51]; }
            throw new TransportException("Unsupported synthetic request.");
        }
        public void Dispose() => disposed = true;
    }
    private EcuIdentity Identity => identity;
}
