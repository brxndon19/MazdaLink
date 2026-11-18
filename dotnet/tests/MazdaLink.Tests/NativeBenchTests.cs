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
        Assert.Equal(new[] { "loopback:1", "supply" }, driver.Calls);
        Assert.Equal(0u, driver.Channel.Diagnostics.LoopbackConfigurationStatus);
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
        public uint SupplyStatus { get; init; }
        public int Supply { get; init; } = 12500;
        public bool LeaveSupplyOutputUntouched { get; init; }
        public bool EmptyInputBuffer { get; private set; }
        public Driver()
        {
            Channel = (NativeBenchMonitorChannel)RuntimeHelpers.GetUninitializedObject(Type);
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
            if (code == 1)
            {
                var list = Type.GetNestedType("ConfigurationList", BindingFlags.NonPublic)!;
                Assert.Equal(1, Marshal.ReadInt32(input));
                nint item = Marshal.ReadIntPtr(input, Marshal.OffsetOf(list, "Items").ToInt32());
                Assert.Equal(3, Marshal.ReadInt32(item)); Assert.Equal(1, Marshal.ReadInt32(item, 4));
                Calls.Add("loopback:1"); return LoopbackStatus;
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
