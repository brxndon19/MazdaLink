using SpeedLink.Core;
using SpeedLink.J2534;
namespace SpeedLink.Tests;

public class BenchFlashTransportTests
{
    [Fact]
    public void mongoose_programming_rejects_an_unregistered_library_before_loading_it()
    {
        var driver = new J2534Driver("Mongoose-Plus ISO3", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll"),
            J2534AdapterKind.MongoosePlusIsoCan3);
        var error = Assert.Throws<TransportException>(() => BenchFlashTransport.Create(driver));
        Assert.Contains("no longer registered", error.Message);
    }

    private sealed class Channel(BenchRequestPermit permit) : IJ2534Channel
    {
        public string? CleanupWarning => null;
        public readonly Queue<J2534Frame> Frames = [];
        public int Writes, Clears, Closes;
        public bool ThrowWrite, ThrowRead;
        public J2534Version Open() => new("synthetic", "synthetic", "04.04");
        public void Connect(uint bitrate, uint requestId, uint responseId) => Assert.Equal((500000u, 0x7E0u, 0x7E8u), (bitrate, requestId, responseId));
        public void ClearReceive() => Clears++;
        public void Write(byte[] request) { permit.Consume(request); Writes++; if (ThrowWrite) throw new IOException("Synthetic ambiguous driver acceptance."); }
        public J2534Frame? Read(int timeoutMilliseconds)
        { if (ThrowRead) throw new IOException("Synthetic driver removed."); if (Frames.TryDequeue(out var frame)) return frame; Thread.Sleep(1); return null; }
        public void Dispose() => Closes++;
    }
    [Theory]
    [InlineData("36616263")] [InlineData("340000000000100000")] [InlineData("23000FFFFF0002")]
    [InlineData("23000000000000")] [InlineData("B100B201")] [InlineData("04")] [InlineData("27021234")]
    [InlineData("1022")] [InlineData("22F190")]
    public void out_of_method_request_never_touches_driver(string hex)
    {
        var permit = new BenchRequestPermit(); var channel = new Channel(permit);
        using var link = new BenchFlashTransport(channel, permit, () => 13500); link.Initialize();
        Assert.Throws<TransportException>(() => link.Exchange(Convert.FromHexString(hex)));
        Assert.Equal(0, channel.Clears); Assert.Equal(0, channel.Writes);
    }
    [Fact]
    public void boot_only_probe_nrc_keeps_j2534_in_sync_for_the_next_probe()
    {
        var permit = new BenchRequestPermit();
        var channel = new Channel(permit);
        channel.Frames.Enqueue(new(0x7E8, 0, [0x7F, 0x09, 0x11]));
        channel.Frames.Enqueue(new(0x7E8, 0, [0x7F, 0x01, 0x11]));
        channel.Frames.Enqueue(new(0x7E8, 0, [0x50, 0x85]));
        using var link = new BenchFlashTransport(channel, permit, () => 13500);
        link.Initialize();
        Assert.Equal(0x11, Assert.Throws<NegativeResponseException>(() => link.Exchange([9, 4])).Code);
        Assert.Equal(0x11, Assert.Throws<NegativeResponseException>(() => link.Exchange([1, 12])).Code);
        Assert.Equal(new byte[] { 0x50, 0x85 }, link.Exchange([0x10, 0x85]));
        Assert.Equal(3, channel.Writes);
    }
    [Fact]
    public void pending_notifications_and_foreign_can_do_not_repeat_request()
    {
        var permit = new BenchRequestPermit(); var channel = new Channel(permit);
        channel.Frames.Enqueue(new(0x7E0, 1, [0x36])); channel.Frames.Enqueue(new(0x7E8, 2, [])); channel.Frames.Enqueue(new(0x7E8, 8, []));
        channel.Frames.Enqueue(new(0x123, 0, [0x76])); channel.Frames.Enqueue(new(0x7E8, 0, [0x7F, 0x36, 0x78]));
        channel.Frames.Enqueue(new(0x7E8, 0, [0x76]));
        using var link = new BenchFlashTransport(channel, permit, () => 13500); link.Initialize();
        Assert.Equal(new byte[] { 0x76 }, link.Exchange([0x36, .. new byte[3968]])); Assert.Equal(1, channel.Writes);
    }
    [Theory]
    [InlineData("timeout")] [InlineData("write")] [InlineData("read")] [InlineData("nrc")] [InlineData("foreign_nrc")] [InlineData("malformed")]
    public void uncertain_or_failed_exchange_latches_transport_without_resend(string fault)
    {
        var permit = new BenchRequestPermit(); var channel = new Channel(permit) { ThrowWrite = fault == "write", ThrowRead = fault == "read" };
        if (fault == "nrc") channel.Frames.Enqueue(new(0x7E8, 0, [0x7F, 0x36, 0x72]));
        if (fault == "foreign_nrc") channel.Frames.Enqueue(new(0x7E8, 0, [0x7F, 0x37, 0x78]));
        if (fault == "malformed") channel.Frames.Enqueue(new(0x7E8, 0x10, [0x76]));
        using var link = new BenchFlashTransport(channel, permit, () => 13500, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)); link.Initialize();
        Assert.ThrowsAny<Exception>(() => link.Exchange([0x36, .. new byte[3968]]));
        Assert.Throws<TransportException>(() => link.Exchange([0x37])); Assert.Equal(1, channel.Writes); Assert.Equal(1, channel.Clears);
    }
}
