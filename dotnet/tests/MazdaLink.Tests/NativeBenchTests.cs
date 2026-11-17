using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        Assert.Equal(new[] { "periodic:5:000FFFFEFF86000000000000", "stop:42", "clear", "write:1000:000FFFFE7AC0000000000000" }, driver.Calls);
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
        { Assert.Equal(8u, code); Calls.Add("clear"); return 0; }
        public Reply Next(object message)
        {
            EmptyInputBuffer = (uint)MessageType.GetField("ProtocolId")!.GetValue(message)! == 0 &&
                ((byte[])MessageType.GetField("Data")!.GetValue(message)!).All(b => b == 0);
            return Reply;
        }
        public void Dispose() => Channel.Dispose();
    }
}
