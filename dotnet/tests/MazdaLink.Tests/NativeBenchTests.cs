using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using MazdaLink.Core;
using MazdaLink.J2534;

namespace MazdaLink.Tests;

public sealed class NativeBenchTests
{
    [Fact]
    public void NativePacketsUseRawExtendedCanAndExactlyTwoFixedPayloads()
    {
        using var driver = new Driver();
        driver.Channel.StartEntryPulse();
        Assert.Throws<TransportException>(() => driver.Channel.RequestIdentity());
        driver.Channel.StopEntryPulse();
        driver.Channel.ClearReceive();
        driver.Channel.RequestIdentity();
        Assert.Throws<TransportException>(() => driver.Channel.RequestIdentity());
        Assert.Throws<TransportException>(() => driver.Channel.StartEntryPulse());
        Assert.Equal(new[] { "periodic:5:000FFFFEFF86000000000000", "stop:42", "supply", "clear", "write:1000:000FFFFE7AC0000000000000" }, driver.Calls);
        Assert.Equal(4152, Marshal.SizeOf(Driver.MessageType));
        Assert.Equal(24, Marshal.OffsetOf(Driver.MessageType, "Data").ToInt32());
    }
    [Theory]
    [InlineData(0u)][InlineData(9u)][InlineData(16u)]
    public void NativeReceivePreservesRealMessagesOnTimeoutLikeStatus(uint status)
    {
        using var driver = new Driver(); driver.Reply = Reply.Identity() with { Status = status };
        var frame = Assert.IsType<RecoveryFrame>(driver.Channel.Read(25));
        Zyj2RecoveryPolicy.RequireMonitorIdentity(frame.Id, frame.Status, frame.Data);
        Assert.True(driver.EmptyInputBuffer);
    }
    [Theory]
    [InlineData(7u, 1u, 5u, 12u, 0u)]
    [InlineData(0u, 2u, 5u, 12u, 0u)]
    [InlineData(9u, 1u, 0u, 12u, 0u)]
    [InlineData(16u, 1u, 6u, 12u, 0u)]
    [InlineData(0u, 1u, 5u, 3u, 0u)]
    [InlineData(0u, 1u, 5u, 13u, 0u)]
    [InlineData(0u, 1u, 5u, 12u, 13u)]
    public void FatalErrorsAndInvalidNativeMetadataNeverBecomeIdentification(uint status, uint count, uint protocol, uint size, uint extra)
    {
        using var driver = new Driver();
        driver.Reply = Reply.Identity() with { Status = status, Count = count, Protocol = protocol, Size = size, ExtraIndex = extra };
        Assert.Throws<TransportException>(() => driver.Channel.Read(25));
    }
    [Fact]
    public void ZeroCountIgnoresTheWholeOutputBuffer()
    {
        using var driver = new Driver(); driver.Reply = Reply.Identity() with { Count = 0, Status = 9 };
        Assert.Null(driver.Channel.Read(25));
    }
    [Fact]
    public void FailedNativeWriteCannotBeRetried()
    {
        using var driver = new Driver { WriteCount = 0 };
        driver.Channel.StartEntryPulse(); driver.Channel.StopEntryPulse();
        Assert.Throws<TransportException>(() => driver.Channel.RequestIdentity());
        Assert.Throws<TransportException>(() => driver.Channel.RequestIdentity());
        Assert.Single(driver.Calls, c => c.StartsWith("write:"));
    }
    [Fact]
    public void UncertainPeriodicStopIsFatalAndCannotReturnToIdentification()
    {
        using var driver = new Driver { StopStatus = 7 };
        driver.Channel.StartEntryPulse();
        Assert.Throws<BenchCleanupException>(() => driver.Channel.StopEntryPulse());
        Assert.Throws<ObjectDisposedException>(() => driver.Channel.RequestIdentity());
        Assert.NotNull(driver.Channel.CleanupWarning);
        Assert.DoesNotContain(driver.Calls, c => c.StartsWith("write:"));
    }
    [Fact]
    public void AdapterDiagnosticsConfigureOnlyLocalEchoAndMeasureSupplyOutsideEntry()
    {
        using var driver = new Driver(); driver.ConfigureDiagnostics();
        Assert.Equal(new[] { "set-loopback:1", "get-loopback", "supply" }, driver.Calls);
        Assert.Equal(0u, driver.Channel.Diagnostics.LoopbackConfigurationStatus);
        Assert.True(driver.LoopbackEnabled);
        Assert.Equal(new BenchConfigurationReading(0, 1), driver.Channel.Diagnostics.LoopbackReadback);
        Assert.Equal(12500, driver.Channel.Diagnostics.SupplyBeforeEntry!.Millivolts);
        driver.Channel.StartEntryPulse();
        driver.Channel.Read(25);
        Assert.Single(driver.Calls, c => c == "supply");
        driver.Channel.StopEntryPulse();
        Assert.Equal(12500, driver.Channel.Diagnostics.SupplyAfterEntry!.Millivolts);
        Assert.Equal(1, driver.Channel.Diagnostics.EntryReceive.Calls);
        Assert.Equal(0, driver.Channel.Diagnostics.IdentityReceive.Calls);
    }
    [Theory]
    [InlineData(0u, 0u, false, false, null, 1u)]
    [InlineData(1u, 0u, false, false, null, 0u)]
    [InlineData(0u, 0u, true, false, null, 0u)]
    [InlineData(0u, 1u, false, false, null, null)]
    [InlineData(0u, 0u, false, true, null, null)]
    [InlineData(0u, 0u, false, false, 2u, null)]
    public void LoopbackSetAndGetAreDistinctAndMissingReadbackCannotClaimEnabled(
        uint setStatus, uint getStatus, bool ignoredSet, bool untouched, uint? overrideValue, uint? expected)
    {
        using var driver = new Driver { LoopbackStatus = setStatus, LoopbackReadStatus = getStatus,
            IgnoreLoopbackSet = ignoredSet, LeaveLoopbackOutputUntouched = untouched, LoopbackReadValue = overrideValue };
        driver.ConfigureDiagnostics();
        Assert.Equal(setStatus, driver.Channel.Diagnostics.LoopbackConfigurationStatus);
        Assert.Equal(new BenchConfigurationReading(getStatus, expected), driver.Channel.Diagnostics.LoopbackReadback);
        Assert.Equal(new[] { "set-loopback:1", "get-loopback", "supply" }, driver.Calls);
        Assert.Equal(0, driver.Channel.Diagnostics.StartCalls);
        Assert.Equal(0, driver.Channel.Diagnostics.IdentityRequests);
    }
    [Theory]
    [InlineData(1u, 12500, false)]
    [InlineData(0u, -1, true)]
    [InlineData(0u, 40000, false)]
    public void UnsupportedOrUnfilledSupplyMeasurementsAreNeverReportedAsZero(uint status, int voltage, bool untouched)
    {
        using var driver = new Driver { SupplyStatus = status, Supply = voltage, LeaveSupplyOutputUntouched = untouched, LoopbackStatus = 1 };
        driver.ConfigureDiagnostics();
        Assert.Null(driver.Channel.Diagnostics.SupplyBeforeEntry!.Millivolts);
        Assert.Equal(status, driver.Channel.Diagnostics.SupplyBeforeEntry!.DriverStatus);
        Assert.Equal(1u, driver.Channel.Diagnostics.LoopbackConfigurationStatus);
    }
    [Fact]
    public void ReceiveStatisticsDistinguishEntryEchoesEmptyReadsAndIdentityWithoutPayloadLogging()
    {
        using var driver = new Driver(); driver.Channel.StartEntryPulse();
        driver.Reply = Reply.Identity() with { RxStatus = 0x101 };
        driver.Channel.Read(25);
        driver.Reply = Reply.Identity() with { Count = 0, Status = 16 };
        driver.Channel.Read(25); driver.Channel.StopEntryPulse();
        driver.Channel.ClearReceive(); driver.Channel.RequestIdentity();
        var data = Reply.Identity().Data.ToArray(); "SECRET12"u8.CopyTo(data.AsSpan(4));
        driver.Reply = Reply.Identity() with { Data = data };
        driver.Channel.Read(25);
        var snapshot = driver.Channel.Diagnostics;
        Assert.Equal(2, snapshot.EntryReceive.Calls); Assert.Equal(1, snapshot.EntryReceive.TxEchoes);
        Assert.Equal(1, snapshot.EntryReceive.EmptyReads); Assert.Equal(1, snapshot.IdentityReceive.ExpectedIdFrames);
        Assert.False(snapshot.IdentityReceive.LastPayloadMatched);
        Assert.Equal(0x21u, snapshot.IdentityReceive.LastCanId);
        Assert.Equal(1, snapshot.IdentityRequests); Assert.NotNull(snapshot.EntryActiveMilliseconds);
        string json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("SECRET12", json); Assert.DoesNotContain("5345435245543132", json);
        Assert.DoesNotContain("Data\"", json); Assert.DoesNotContain("LibraryPath", json);
    }
    [Fact]
    public void EntryTrafficIsGroupedIntoBoundedTimeBins()
    {
        using var driver = new Driver(); driver.Channel.StartEntryPulse();
        driver.Reply = Reply.Identity() with { RxStatus = 0x101 };
        driver.SetEntryAge(3); driver.Channel.Read(25);
        driver.Reply = Reply.Identity();
        driver.SetEntryAge(7); driver.Channel.Read(25);
        driver.SetEntryAge(1000); driver.Channel.Read(25);
        var bins = driver.Channel.Diagnostics.EntryTraffic;
        Assert.Equal(new[] { 3, 7, 31 }, bins.Select(b => b.Second));
        Assert.Equal(1, bins[0].TxEchoes); Assert.Equal(1, bins[1].OtherReturnedFrames);
        Assert.Equal(1, bins[2].OtherReturnedFrames);
        Assert.InRange(bins.Count, 0, 32);
    }
    [Fact]
    public void IdentificationChannelCannotAcquireTheSeparateRamCapability()
    {
        using var driver = new Driver(); driver.IdentifyMonitor();
        Assert.Throws<TransportException>(() => driver.SendRamStep(0));
        Assert.Single(driver.Calls, c => c.StartsWith("write:"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RamChannelRequiresAnActualMonitorReplyRatherThanAnEcho(bool echo)
    {
        using var driver = new Driver(allowRamSelfTest: true);
        if (echo) { driver.Reply = Reply.Identity() with { RxStatus = 0x101 }; driver.IdentifyMonitor(); }
        Assert.Throws<TransportException>(() => driver.SendRamStep(0));
    }
    [Fact]
    public void RamStepsCannotSkipReadbackOrAdvancePastAnUnansweredAddress()
    {
        using var skip = new Driver(allowRamSelfTest: true); skip.IdentifyMonitor();
        int execute = Zyj2RamSelfTestPolicy.Steps.ToList().FindIndex(s => s.Operation == RamSelfTestOperation.ExecuteFixedTest);
        Assert.Throws<TransportException>(() => skip.SendRamStep(execute));
        using var waiting = new Driver(allowRamSelfTest: true); waiting.IdentifyMonitor(); waiting.SendRamStep(0);
        Assert.Throws<TransportException>(() => waiting.SendRamStep(1));
        waiting.SetRamReply(0); waiting.Channel.Read(25);
        Assert.Throws<TransportException>(() => waiting.SendRamStep(1));
    }
    [Fact]
    public void NativeRamCapabilityRequiresAllOneHundredTwentyByteRepliesBeforeExecutionAndCannotRestart()
    {
        using var driver = new Driver(allowRamSelfTest: true); driver.IdentifyMonitor();
        for (int i = 0; i < Zyj2RamSelfTestPolicy.Steps.Count; i++)
        {
            driver.SendRamStep(i);
            if (Zyj2RamSelfTestPolicy.Steps[i].ExpectsReply)
            { driver.SetRamReply(i); driver.Channel.Read(25); }
        }
        Assert.Equal(new RamSelfTestTransportDiagnostics(264, 120, false, false), driver.RamDiagnostics);
        Assert.Equal(2, driver.Channel.Diagnostics.IdentityRequests);
        Assert.Throws<TransportException>(() => driver.SendRamStep(0));
    }
    [Fact]
    public void NativeRamByteMismatchPermanentlyBlocksLaterSteps()
    {
        using var driver = new Driver(allowRamSelfTest: true); driver.IdentifyMonitor();
        int mismatch = Zyj2RamSelfTestPolicy.Steps.ToList().FindIndex(s => s.Operation == RamSelfTestOperation.VerifyByte);
        for (int i = 0; i <= mismatch; i++)
        {
            driver.SendRamStep(i);
            if (!Zyj2RamSelfTestPolicy.Steps[i].ExpectsReply) continue;
            driver.SetRamReply(i);
            if (i == mismatch) driver.Reply.Data[6] ^= 1;
            driver.Channel.Read(25);
        }
        Assert.True(driver.RamDiagnostics.Failed);
        Assert.Throws<TransportException>(() => driver.SendRamStep(mismatch + 1));
    }
    [Fact]
    public void ProtectedInitializationCannotBeAcquiredByIdentificationOrReadOnlyRamCapabilities()
    {
        foreach (bool ram in new[] { false, true })
        {
            using var driver = new Driver(allowRamSelfTest: ram); driver.IdentifyMonitor();
            Assert.Throws<TransportException>(() => driver.SendInitStep(0));
        }
        using var init = new Driver(allowProtectedInitialization: true); init.IdentifyMonitor();
        Assert.Throws<TransportException>(() => init.SendRamStep(0));
        Assert.False(typeof(IFixedRamSelfTestChannel).IsAssignableFrom(typeof(NativeProtectedInitializationChannel)));
        Assert.False(typeof(IProtectedInitializationChannel).IsAssignableFrom(typeof(NativeRamSelfTestChannel)));
    }
    [Fact]
    public void ProtectedInitializationNativeSequenceRequiresEveryByteAndCannotRestart()
    {
        using var driver = new Driver(allowProtectedInitialization: true); driver.IdentifyMonitor();
        for (int i = 0; i < Zyj2ProtectedInitializationPolicy.Steps.Count; i++)
        {
            driver.SendInitStep(i);
            if (Zyj2ProtectedInitializationPolicy.Steps[i].ExpectsReply)
            { driver.SetInitReply(i); driver.Channel.Read(25); }
        }
        Assert.Equal(new RamSelfTestTransportDiagnostics(2032, 936, false, false), driver.RamDiagnostics);
        Assert.Equal(2, driver.Channel.Diagnostics.IdentityRequests);
        Assert.Throws<TransportException>(() => driver.SendInitStep(0));
    }
    [Fact]
    public void ProtectedInitializationCannotSkipReadbackOrAdvancePastAPendingReply()
    {
        using var skipped = new Driver(allowProtectedInitialization: true); skipped.IdentifyMonitor();
        int execute = Zyj2ProtectedInitializationPolicy.Steps.ToList().FindIndex(s => s.Operation == RamSelfTestOperation.ExecuteFixedTest);
        Assert.Throws<TransportException>(() => skipped.SendInitStep(execute));
        using var pending = new Driver(allowProtectedInitialization: true); pending.IdentifyMonitor(); pending.SendInitStep(0);
        Assert.Throws<TransportException>(() => pending.SendInitStep(1));
        using var noMonitor = new Driver(allowProtectedInitialization: true);
        Assert.Throws<TransportException>(() => noMonitor.SendInitStep(0));
    }
    [Fact]
    public void ProtectedInitializationNativeMismatchPermanentlyBlocksExecution()
    {
        using var driver = new Driver(allowProtectedInitialization: true); driver.IdentifyMonitor();
        for (int i = 0; ; i++)
        {
            driver.SendInitStep(i); var step = Zyj2ProtectedInitializationPolicy.Steps[i];
            if (!step.ExpectsReply) continue;
            driver.SetInitReply(i);
            if (step.Operation == RamSelfTestOperation.VerifyByte)
            {
                driver.Reply.Data[6] ^= 1; driver.Channel.Read(25);
                Assert.True(driver.RamDiagnostics.Failed);
                Assert.Throws<TransportException>(() => driver.SendInitStep(i + 1)); break;
            }
            driver.Channel.Read(25);
        }
    }

    public sealed record Reply(uint Status, uint Count, uint Protocol, uint RxStatus, uint Size, uint ExtraIndex, byte[] Data)
    {
        public static Reply Identity()
        {
            byte[] data = new byte[4128]; BinaryPrimitives.WriteUInt32BigEndian(data, 0x21);
            new byte[] { 0x7A, 0xC6, 0x50, 0x79, 0x77, 0x30, 0x58, 0x41 }.CopyTo(data, 4);
            return new(0, 1, 5, 0x100, 12, 0, data);
        }
    }
    private sealed class Driver : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Type Type = typeof(NativeBenchMonitorChannel);
        public static readonly Type MessageType = Type.GetNestedType("Message", BindingFlags.NonPublic)!;
        public NativeBenchMonitorChannel Channel { get; }
        public List<string> Calls { get; } = [];
        public Reply Reply { get; set; } = Reply.Identity();
        public uint WriteCount { get; init; } = 1;
        public uint StopStatus { get; init; }
        public uint LoopbackStatus { get; init; }
        public uint LoopbackReadStatus { get; init; }
        public uint? LoopbackReadValue { get; init; }
        public bool IgnoreLoopbackSet { get; init; }
        public bool LeaveLoopbackOutputUntouched { get; init; }
        public bool LoopbackEnabled { get; private set; }
        public uint SupplyStatus { get; init; }
        public int Supply { get; init; } = 12500;
        public bool LeaveSupplyOutputUntouched { get; init; }
        public bool EmptyInputBuffer { get; private set; }
        public Driver(bool allowRamSelfTest = false, bool allowProtectedInitialization = false)
        {
            Channel = (NativeBenchMonitorChannel)RuntimeHelpers.GetUninitializedObject(Type);
            Set("allowRamSelfTest", allowRamSelfTest);
            Set("allowProtectedInitialization", allowProtectedInitialization);
            Set("opened", true); Set("connected", true); Set("filtered", true);
            BindMessage("write", nameof(Write)); BindMessage("startPeriodic", nameof(Start));
            var stop = Type.GetField("stopPeriodic", Hidden)!;
            Set("stopPeriodic", Delegate.CreateDelegate(stop.FieldType, this, nameof(Stop)));
            var ioctl = Type.GetField("ioctl", Hidden)!;
            Set("ioctl", Delegate.CreateDelegate(ioctl.FieldType, this, nameof(Ioctl)));
            var method = Function("read"); var il = method.GetILGenerator(); var reply = il.DeclareLocal(typeof(Reply));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldobj, MessageType); il.Emit(OpCodes.Box, MessageType);
            il.Emit(OpCodes.Call, typeof(Driver).GetMethod(nameof(Next))!); il.Emit(OpCodes.Stloc, reply);
            foreach (var (field, property) in new[] { ("ProtocolId", "Protocol"), ("RxStatus", "RxStatus"), ("DataSize", "Size"), ("ExtraDataIndex", "ExtraIndex"), ("Data", "Data") })
            {
                il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloc, reply);
                il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty(property)!.GetMethod!);
                il.Emit(OpCodes.Stfld, MessageType.GetField(field)!);
            }
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldloc, reply);
            il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty("Count")!.GetMethod!); il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldloc, reply); il.Emit(OpCodes.Callvirt, typeof(Reply).GetProperty("Status")!.GetMethod!); il.Emit(OpCodes.Ret);
            Bind("read", method);
        }
        private void Set(string name, object? value) => Type.GetField(name, Hidden)!.SetValue(Channel, value);
        public void ConfigureDiagnostics() => Type.GetMethod("ConfigureDiagnostics", Hidden)!.Invoke(Channel, null);
        public RamSelfTestTransportDiagnostics RamDiagnostics => (RamSelfTestTransportDiagnostics)Type.GetProperty("RamDiagnostics", Hidden)!.GetValue(Channel)!;
        public void IdentifyMonitor()
        { Channel.StartEntryPulse(); Channel.StopEntryPulse(); Channel.ClearReceive(); Channel.RequestIdentity(); Channel.Read(25); }
        public void SendRamStep(int index)
        {
            try { Type.GetMethod("SendFixedRamSelfTestStep", Hidden)!.Invoke(Channel, [index]); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        public void SendInitStep(int index)
        {
            try { Type.GetMethod("SendProtectedInitializationStep", Hidden)!.Invoke(Channel, [index]); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        public void SetInitReply(int index)
        {
            var step = Zyj2ProtectedInitializationPolicy.Steps[index];
            byte[] payload = step.Operation switch
            {
                RamSelfTestOperation.Address => step.Payload,
                RamSelfTestOperation.VerifyByte => [0x7A, 0xB1, Zyj2ProtectedInitializationPolicy.Image[step.ImageOffset], 0, 0, 0, 0, 0],
                RamSelfTestOperation.ExecuteFixedTest => [0x7A, 0xC6, 0x4D, 0x49, 0, 0, 0, 0],
                RamSelfTestOperation.IdentifyMonitor => Zyj2RecoveryPolicy.ExpectedIdentity,
                _ => throw new Exception("Uploads have no success acknowledgement")
            };
            Reply = new(0, 1, 5, 0x100, 12, 12, [0, 0, 0, 0x21, ..payload]);
        }
        public void SetRamReply(int index)
        {
            var step = Zyj2RamSelfTestPolicy.Steps[index]; byte[] data = Reply.Identity().Data;
            byte[] p = step.Operation switch
            {
                RamSelfTestOperation.Address => step.Payload,
                RamSelfTestOperation.VerifyByte => [0x7A, 0xB1, Zyj2RamSelfTestPolicy.Image[step.ImageOffset], 0, 0, 0, 0, 0],
                RamSelfTestOperation.ExecuteFixedTest => [0x7A, 0xC6, 0x4D, 0x50, 1, 0x80, 0, 0x0A],
                RamSelfTestOperation.IdentifyMonitor => Zyj2RecoveryPolicy.ExpectedIdentity,
                _ => throw new Exception("No upload-success acknowledgement exists")
            };
            p.CopyTo(data, 4); Reply = Reply.Identity() with { Data = data };
        }
        public void SetEntryAge(int seconds) => Set("entryStarted", Stopwatch.GetTimestamp() - Stopwatch.Frequency * seconds);
        private static DynamicMethod Function(string name) => new(name, typeof(uint),
            [typeof(Driver), typeof(uint), MessageType.MakeByRefType(), typeof(uint).MakeByRefType(), typeof(uint)], Type, true);
        private void Bind(string name, DynamicMethod method) => Set(name, method.CreateDelegate(Type.GetField(name, Hidden)!.FieldType, this));
        private void BindMessage(string name, string callback)
        {
            var method = Function(name); var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldobj, MessageType); il.Emit(OpCodes.Box, MessageType);
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Call, typeof(Driver).GetMethod(callback)!); il.Emit(OpCodes.Ret); Bind(name, method);
        }
        private string Packet(object message)
        {
            Assert.Equal(5u, MessageType.GetField("ProtocolId")!.GetValue(message));
            Assert.Equal(0x100u, MessageType.GetField("TxFlags")!.GetValue(message));
            Assert.Equal(12u, MessageType.GetField("DataSize")!.GetValue(message));
            return Convert.ToHexString(((byte[])MessageType.GetField("Data")!.GetValue(message)!).AsSpan(0, 12));
        }
        public uint Start(object message, ref uint identifier, uint interval)
        { Calls.Add($"periodic:{interval}:{Packet(message)}"); identifier = 42; return 0; }
        public uint Write(object message, ref uint count, uint timeout)
        { Assert.Equal(1u, count); Calls.Add($"write:{timeout}:{Packet(message)}"); count = WriteCount; return 0; }
        public uint Stop(uint channel, uint identifier) { Calls.Add($"stop:{identifier}"); return StopStatus; }
        public uint Ioctl(uint channel, uint code, nint input, nint output)
        {
            if (code == 3)
            {
                Calls.Add("supply");
                if (!LeaveSupplyOutputUntouched) Marshal.WriteInt32(output, Supply);
                return SupplyStatus;
            }
            // ABI reference: https://quantexlab.de/en/develop/j2534/pt_ioctl_config.html
            // Independently model GET reading state and SET changing it.
            // Using GET where SET is required must leave loopback disabled.
            if (code is 1 or 2)
            {
                var list = Type.GetNestedType("ConfigurationList", BindingFlags.NonPublic)!;
                Assert.Equal(1, Marshal.ReadInt32(input));
                nint item = Marshal.ReadIntPtr(input, Marshal.OffsetOf(list, "Items").ToInt32());
                Assert.Equal(3, Marshal.ReadInt32(item)); Assert.Equal(nint.Zero, output);
                if (code == 2)
                {
                    Assert.Equal(1, Marshal.ReadInt32(item, 4));
                    Calls.Add("set-loopback:1");
                    if (LoopbackStatus == 0 && !IgnoreLoopbackSet) LoopbackEnabled = true;
                    return LoopbackStatus;
                }
                Assert.Equal(-1, Marshal.ReadInt32(item, 4));
                Calls.Add("get-loopback");
                if (!LeaveLoopbackOutputUntouched)
                    Marshal.WriteInt32(item, 4, (int)(LoopbackReadValue ?? (LoopbackEnabled ? 1u : 0u)));
                return LoopbackReadStatus;
            }
            Assert.Equal(8u, code); Calls.Add("clear"); return 0;
        }
        public Reply Next(object message)
        {
            EmptyInputBuffer = (uint)MessageType.GetField("ProtocolId")!.GetValue(message)! == 0 &&
                ((byte[])MessageType.GetField("Data")!.GetValue(message)!).All(b => b == 0);
            return Reply;
        }
        public void Dispose() => Channel.Dispose();
    }
}
