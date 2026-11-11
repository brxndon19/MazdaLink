using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public class VLinkerStartupTests
{
    [Fact]
    public void ReconnectFindsRetainedTransferBaudAndRestoresDefaultOnClose()
    {
        using var serial = new RateSensitiveAdapter { DeviceBaud = VLinkerReadTransport.TransferBaud };
        serial.IdentityReplies.Enqueue("ATI\r?\r>");
        using var link = new VLinkerReadTransport(serial, new TestClock());

        link.Initialize();

        Assert.True(link.Ready);
        Assert.Equal(VLinkerReadTransport.TransferBaud, link.Baud);
        Assert.Equal(new[]
        {
            (VLinkerReadTransport.DefaultBaud, "ATI"),
            (VLinkerReadTransport.TransferBaud, "ATI"),
            (VLinkerReadTransport.TransferBaud, "ATI"),
            (VLinkerReadTransport.TransferBaud, "ATE0"),
            (VLinkerReadTransport.TransferBaud, "ATI"),
            (VLinkerReadTransport.TransferBaud, "VTI")
        }, serial.Writes.Take(6));
        Assert.DoesNotContain(serial.Writes, write => write.Command.StartsWith("STPX", StringComparison.Ordinal));
        Assert.DoesNotContain(serial.Writes, write => write.Command.StartsWith("STSBR", StringComparison.Ordinal));
        link.Dispose();
        Assert.Null(link.CloseWarning);
        Assert.Equal(VLinkerReadTransport.DefaultBaud, serial.DeviceBaud);
    }

    [Fact]
    public void SelectedRateIsTriedFirstAndAStaleSelectionCanFindTheDefault()
    {
        using var serial = new RateSensitiveAdapter { BaudRate = VLinkerReadTransport.TransferBaud };
        using var link = new VLinkerReadTransport(serial, new TestClock());

        link.Initialize();

        Assert.True(link.Ready);
        Assert.Equal(new[]
        {
            (VLinkerReadTransport.TransferBaud, "ATI"),
            (VLinkerReadTransport.DefaultBaud, "ATI"),
            (VLinkerReadTransport.DefaultBaud, "ATE0")
        }, serial.Writes.Take(3));
        Assert.Equal(VLinkerReadTransport.TransferBaud, serial.DeviceBaud);
    }

    [Fact]
    public void AnUnpoweredAdapterIsProbedOnceAtEachDistinctRateWithoutDiagnosticTraffic()
    {
        using var serial = new RateSensitiveAdapter { DeviceBaud = 0, BaudRate = 57600 };
        var clock = new TestClock();
        using var link = new VLinkerReadTransport(serial, clock);

        var error = Assert.Throws<TransportException>(link.Initialize);

        Assert.Contains("Check the USB connection, adapter power and selected COM port", error.Message);
        Assert.Equal(new[]
        {
            (57600, "ATI"),
            (VLinkerReadTransport.TransferBaud, "ATI"),
            (VLinkerReadTransport.DefaultBaud, "ATI")
        }, serial.Writes);
        Assert.InRange(clock.Elapsed.TotalSeconds, 8, 8.1);
        Assert.True(link.Failed);
        Assert.False(link.Ready);
    }

    [Theory]
    [InlineData("ELM327 v2.3\r")]
    [InlineData("UNEXPECTED\r>")]
    [InlineData("?\r>")]
    public void PartialOrUnexpectedRepliesFailClosed(string reply)
    {
        using var serial = new RateSensitiveAdapter();
        serial.IdentityReplies.Enqueue(reply);
        serial.IdentityReplies.Enqueue(reply);
        using var link = new VLinkerReadTransport(serial, new TestClock());

        Assert.Throws<TransportException>(link.Initialize);

        Assert.False(link.Ready);
        Assert.True(link.Failed);
        Assert.Equal(reply == "?\r>" ? 2 : 1, serial.Writes.Count);
        Assert.All(serial.Writes, write => Assert.Equal((VLinkerReadTransport.DefaultBaud, "ATI"), write));
    }

    private sealed class RateSensitiveAdapter : ISerialConnection
    {
        private readonly FakeSerial inner = new();
        private readonly Queue<byte> pending = new();
        public Queue<string> IdentityReplies { get; } = new();
        public List<(int Baud, string Command)> Writes { get; } = new();
        public int BaudRate { get; set; } = VLinkerReadTransport.DefaultBaud;
        public int DeviceBaud { get; set; } = VLinkerReadTransport.DefaultBaud;
        public bool IsOpen => inner.IsOpen;
        public int BytesToRead => pending.Count + inner.BytesToRead;

        public int Write(byte[] data)
        {
            string command = Encoding.ASCII.GetString(data).TrimEnd('\r');
            Writes.Add((BaudRate, command));
            if (BaudRate != DeviceBaud)
                return data.Length;
            if (command == "ATI" && IdentityReplies.TryDequeue(out string? reply))
            {
                foreach (byte value in Encoding.ASCII.GetBytes(reply))
                    pending.Enqueue(value);
                return data.Length;
            }
            if (command.StartsWith("STSBR ", StringComparison.Ordinal))
                DeviceBaud = int.Parse(command.AsSpan(6), System.Globalization.CultureInfo.InvariantCulture);
            return inner.Write(data);
        }

        public int Read(byte[] data, int offset, int count)
        {
            int read = 0;
            while (read < count && pending.TryDequeue(out byte value))
                data[offset + read++] = value;
            return read + inner.Read(data, offset + read, count - read);
        }

        public void DiscardInput() { pending.Clear(); inner.DiscardInput(); }
        public void Flush() => inner.Flush();
        public void Dispose() => inner.Dispose();
    }
}
