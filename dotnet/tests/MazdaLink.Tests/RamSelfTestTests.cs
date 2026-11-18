using System.Buffers.Binary;
using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class RamSelfTestTests
{
    [Theory]
    [InlineData(0x00, 0x00, 0)] [InlineData(0x80, 0x00, 1)] [InlineData(0x90, 0xAA, 2)] [InlineData(0x00, 0x00, 255)]
    public void FixedSh2ProgramOnlyWritesItsOwnRamResultAndReturnsToMonitor(int fccs, int mat, int hardwareClass)
    {
        // Small independent instruction interpreter: reject every instruction,
        // memory access or branch outside this program's explicit contract.
        byte[] memory = Zyj2RamSelfTestPolicy.Image;
        uint start = 0xFFFF8000, pc = start; var registers = new uint[16];
        var reads = new List<uint>(); var writes = new List<uint>(); var calls = new List<uint>(); byte[]? result = null;
        byte Read(uint address)
        {
            if (address >= start && address < start + memory.Length) return memory[address - start];
            reads.Add(address);
            return address switch { 0xFFFFE800 => (byte)fccs, 0xFFFFE805 => (byte)mat, _ => throw new Exception("Unexpected register read") };
        }
        ushort Word(uint a) => (ushort)((Read(a) << 8) | Read(a + 1));
        int instructions = 0; bool returned = false;
        while (!returned && instructions++ < 32)
        {
            ushort op = Word(pc); int n = (op >> 8) & 15;
            if ((op & 0xF000) == 0xD000)
            {
                uint a = ((pc + 4) & ~3u) + (uint)(op & 255) * 4;
                registers[n] = ((uint)Word(a) << 16) | Word(a + 2);
            }
            else if ((op & 0xF000) == 0x9000) registers[n] = unchecked((uint)(short)Word(pc + 4 + (uint)(op & 255) * 2));
            else if ((op & 0xF000) == 0xE000) registers[n] = unchecked((uint)(sbyte)(byte)op);
            else if ((op & 0xF000) == 0x7000) registers[n] = unchecked(registers[n] + (uint)(sbyte)(byte)op);
            else if (op == 0x6010) registers[0] = unchecked((uint)(sbyte)Read(registers[1]));
            else if ((op & 0xFF00) == 0x8000)
            {
                uint a = registers[(op >> 4) & 15] + (uint)(op & 15);
                Assert.InRange(a, start + 61, start + 63);
                writes.Add(a); memory[a - start] = (byte)registers[0];
            }
            else if (op == 0x430B)
            {
                calls.Add(registers[3]); Assert.Equal(0x0009, Word(pc + 2));
                if (registers[3] == 0x0EE4)
                {
                    registers[0] = (uint)hardwareClass;
                    registers[6] = 0xDEADBEEF; // Caller must load its result pointer after this call.
                }
                else
                {
                    Assert.Equal(0x04C2u, registers[3]);
                    Assert.Equal(0xC0u, registers[4] & 255); Assert.Equal(6u, registers[5]);
                    result = Enumerable.Range(0, 6).Select(i => Read(registers[6] + (uint)i)).ToArray();
                }
                pc += 2; // Delay slot is a verified NOP; resident sender returns here.
            }
            else if (op == 0x432B)
            {
                Assert.Equal(0x0040u, registers[3]); Assert.Equal(0x0924u, registers[4]);
                calls.Add(registers[3]);
                Assert.Equal(0x0009, Word(pc + 2)); returned = true;
            }
            else throw new Exception($"Unexpected instruction {op:X4}");
            pc += 2;
        }
        Assert.True(returned);
        Assert.Equal(new uint[] { 0xFFFFE800, 0xFFFFE805 }, reads);
        Assert.Equal(new uint[] { start + 61, start + 62, start + 63 }, writes);
        Assert.Equal(new uint[] { 0x0EE4, 0x04C2, 0x0040 }, calls);
        Assert.Equal(new byte[] { 0x4D, 0x4C, 0x32, (byte)hardwareClass, (byte)fccs, (byte)mat }, result);
    }

    [Fact]
    public void ProgramAndScriptCannotBeChangedThroughReturnedArrays()
    {
        byte[] image = Zyj2RamSelfTestPolicy.Image; image[0] ^= 1;
        byte[] frame = Zyj2RamSelfTestPolicy.Steps[0].Payload; frame[2] = 0;
        Assert.Equal(Zyj2RamSelfTestPolicy.ImageSha256, RomStorage.Hash(Zyj2RamSelfTestPolicy.Image));
        Assert.Equal(0xFF, Zyj2RamSelfTestPolicy.Steps[0].Payload[2]);
    }

    [Fact]
    public void ScriptUploadsOnlyItsFixedRamImageAndReadsBackEveryByteBeforeExecute()
    {
        using var peer = new Peer();
        var result = new Zyj2RamSelfTest().Run(peer, _ => { });
        Assert.Equal(Zyj2RamSelfTestPolicy.Image, peer.Memory);
        Assert.Equal(64, result.UploadedBytes); Assert.Equal(64, result.VerifiedBytes);
        Assert.Equal(1, result.HardwareClass);
        Assert.Equal(Enumerable.Range(0, 64), peer.VerifiedOffsets);
        Assert.Single(peer.Executed); Assert.True(result.FlashWriteEnableObserved);
        Assert.False(result.FlashErrorObserved); Assert.False(result.UserBootMatSelected);
        Assert.Equal(RamSelfTestOperation.IdentifyMonitor, peer.Sent.Last());
    }

    [Theory]
    [InlineData(0)] [InlineData(31)] [InlineData(63)]
    public void AnyByteMismatchPreventsExecution(int offset)
    {
        using var peer = new Peer { CorruptByte = offset };
        Assert.Throws<TransportException>(() => new Zyj2RamSelfTest().Run(peer, _ => { }));
        Assert.Empty(peer.Executed);
    }

    [Fact]
    public void UploadErrorIsNotLostBeforeReadbackAndCannotReachExecution()
    {
        using var peer = new Peer { UploadError = true };
        Assert.Throws<TransportException>(() => new Zyj2RamSelfTest().Run(peer, _ => { }));
        Assert.Empty(peer.Executed);
    }

    [Fact]
    public void CancellationAfterReadbackCannotExecute()
    {
        using var peer = new Peer(); using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new Zyj2RamSelfTest().Run(peer, stage =>
        { if (stage == "every-fixed-ram-image-byte-verified") cancel.Cancel(); }, cancel.Token));
        Assert.Equal(64, peer.VerifiedOffsets.Count); Assert.Empty(peer.Executed);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public void WrongExecutionMarkerOrMonitorReturnCannotBecomeSuccess(bool marker, bool monitor)
    {
        using var peer = new Peer { BadMarker = marker, BadMonitorReturn = monitor };
        var stages = new List<string>();
        Assert.Throws<TransportException>(() => new Zyj2RamSelfTest().Run(peer, stages.Add));
        Assert.DoesNotContain("monitor-return-confirmed-after-fixed-ram-self-test", stages);
    }

    [Fact]
    public void ExternalCodeAddressOverridesAndMissingBackupsCannotOpenEitherBackend()
    {
        int opens = 0;
        var terminal = new Terminal(() => { opens++; throw new Exception(); }, new StringReader(""),
            new StringWriter(), Path.GetTempPath(), ramSelfTest: () => { opens++; throw new Exception(); });
        string absent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "original.rom");
        Assert.Equal(1, terminal.Execute(["ram-self-test", absent]));
        Assert.Equal(1, terminal.Execute(["ram-self-test", absent, "external.bin"]));
        Assert.Equal(1, terminal.Execute(["ram-self-test", absent, "--address", "0"]));
        Assert.Equal(0, opens);
    }

    [Fact]
    public void ColdPowerOffWaitCompletesBeforeStartAndCancellationStopsSetup()
    {
        var stages = new List<string>(); int waits = 0;
        BenchColdStart.Prepare(new StringReader("POWER OFF\nSTART\n"), new StringWriter(), stages.Add,
            wait: (ms, _) => { Assert.Equal(60000, ms); Assert.Single(stages); waits++; });
        Assert.Equal(1, waits); Assert.Equal(2, stages.Count);
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => BenchColdStart.Prepare(new StringReader("POWER OFF\nSTART\n"),
            new StringWriter(), _ => { }, cancel.Token, (_, _) => cancel.Cancel()));
        Assert.Throws<InvalidDataException>(() => BenchColdStart.Prepare(new StringReader("START\n"),
            new StringWriter(), _ => throw new Exception("Must not arm")));
    }
    [Fact]
    public void NormalRestartWaitsSixtyThenTenSecondsWithExplicitPowerConfirmations()
    {
        var waits = new List<int>(); var stages = new List<string>();
        BenchColdStart.PrepareNormalRestart(new StringReader("POWER OFF\nPOWER ON\n"), new StringWriter(), stages.Add,
            wait: (ms, _) => waits.Add(ms));
        Assert.Equal(new[] { 60000, 10000 }, waits);
        Assert.Equal(new[] { "normal-restart-operator-power-off-confirmed", "normal-restart-cold-host-wait-completed",
            "normal-restart-operator-power-on-confirmed", "normal-restart-startup-host-wait-completed" }, stages);
    }
    [Theory]
    [InlineData(60000)] [InlineData(10000)]
    public void CancelledNormalRestartCannotCompleteStartup(int phase)
    {
        using var cancel = new CancellationTokenSource(); var stages = new List<string>();
        Assert.Throws<OperationCanceledException>(() => BenchColdStart.PrepareNormalRestart(
            new StringReader("POWER OFF\nPOWER ON\n"), new StringWriter(), stages.Add, cancel.Token,
            (ms, _) => { if (ms == phase) cancel.Cancel(); }));
        Assert.DoesNotContain("normal-restart-startup-host-wait-completed", stages);
    }
    [Fact]
    public void WrongNormalRestartPowerConfirmationCannotStartDiagnosticsWait()
    {
        var waits = new List<int>();
        Assert.Throws<InvalidDataException>(() => BenchColdStart.PrepareNormalRestart(new StringReader("START\n"),
            new StringWriter(), _ => { }, wait: (ms, _) => waits.Add(ms)));
        Assert.Empty(waits);
        Assert.Throws<InvalidDataException>(() => BenchColdStart.PrepareNormalRestart(new StringReader("POWER OFF\nSTART\n"),
            new StringWriter(), _ => { }, wait: (ms, _) => waits.Add(ms)));
        Assert.Equal(new[] { 60000 }, waits);
    }

    private sealed class Peer : IFixedRamSelfTestChannel
    {
        public byte[] Memory { get; } = new byte[64];
        public List<int> VerifiedOffsets { get; } = [];
        public List<int> Executed { get; } = [];
        public List<RamSelfTestOperation> Sent { get; } = [];
        public int? CorruptByte { get; init; }
        public bool UploadError { get; init; }
        public bool BadMarker { get; init; }
        public bool BadMonitorReturn { get; init; }
        private uint address, uploadAddress;
        private readonly Queue<RecoveryFrame> replies = new();
        public string? CleanupWarning => null;
        public void Open() => throw new Exception("Entry is a separate workflow");
        public void StartEntryPulse() => throw new Exception();
        public void StopEntryPulse() => throw new Exception();
        public void ClearReceive() => throw new Exception("Pending upload errors must not be discarded");
        public void RequestIdentity() => throw new Exception();
        public void SendSelfTestStep(int index)
        {
            var step = Zyj2RamSelfTestPolicy.Steps[index]; var p = step.Payload;
            Sent.Add(step.Operation); Assert.Equal(8, p.Length); Assert.Equal(0x7A, p[0]);
            switch (step.Operation)
            {
                case RamSelfTestOperation.Address:
                    Assert.Equal(0x9C, p[1]); address = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2)); uploadAddress = address;
                    Assert.InRange(address, 0xFFFF8000u, 0xFFFF803Fu);
                    p[6] = 0x58; p[7] = 0x41; Reply(p); break;
                case RamSelfTestOperation.Upload:
                    int count = p[1] & 7; Assert.Equal(0xA8, p[1] & 0xF8);
                    Assert.InRange(uploadAddress, 0xFFFF8000u, 0xFFFF803Fu);
                    p.AsSpan(2, count).CopyTo(Memory.AsSpan((int)(uploadAddress - 0xFFFF8000))); uploadAddress += (uint)count;
                    if (UploadError && step.ImageOffset == 6) Reply([0x7A, 0xA1, 2, 0, 0, 0, 0, 0]);
                    break;
                case RamSelfTestOperation.VerifyByte:
                    Assert.Equal(0xB4, p[1]); Assert.Equal(address + 1, BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(2)));
                    int offset = (int)(address - 0xFFFF8000); VerifiedOffsets.Add(offset);
                    byte value = Memory[offset]; if (offset == CorruptByte) value ^= 1;
                    Reply([0x7A, 0xB1, value, 9, 8, 7, 6, 5]); break;
                case RamSelfTestOperation.ExecuteFixedTest:
                    Assert.Equal(64, VerifiedOffsets.Count); Assert.Equal(0xFFFF8000u, address);
                    Assert.Equal(Zyj2RamSelfTestPolicy.Image, Memory); Executed.Add(index);
                    Reply([0x7A, 0xC6, BadMarker ? (byte)0 : (byte)0x4D, 0x4C, 0x32, 1, 0x80, 0]); break;
                case RamSelfTestOperation.IdentifyMonitor:
                    byte[] id = Zyj2RecoveryPolicy.ExpectedIdentity; if (BadMonitorReturn) id[7] ^= 1;
                    Reply(id); break;
                default: throw new Exception();
            }
        }
        private void Reply(byte[] bytes) => replies.Enqueue(new(0x21, 0x100, bytes));
        public RecoveryFrame? Read(int timeoutMilliseconds) => replies.TryDequeue(out var frame) ? frame : null;
        public void Dispose() { }
    }
}
