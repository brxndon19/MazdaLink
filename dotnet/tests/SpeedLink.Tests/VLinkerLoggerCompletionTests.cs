using System.Text;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class VLinkerLoggerCompletionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16384)]
    public void Valid_reply_completes_before_idle_timeout_without_repeating_the_ecu_request(int fragment)
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { Fragment = fragment };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        int started = clock.Now;
        Assert.Equal(Fixture.Hex("62000C1F40"), ((IReadTransport)link).ExchangeLogger([0x22, 0, 0x0C], 5));
        Assert.True(clock.Now - started < 100); Assert.Single(serial.Requests);
        Assert.Contains("!", serial.Controls); Assert.DoesNotContain("\r", serial.Controls); Assert.False(link.Failed);
        link.ExchangeLogger([0x22, 0, 0x0C], 5); Assert.Equal(2, serial.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Natural_prompt_race_never_sends_a_repeat_command(bool readyWithReply)
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { NaturalRace = !readyWithReply, ReadyWithReply = readyWithReply };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        link.ExchangeLogger([0x22, 0, 0x0C], 5);
        Assert.Single(serial.Requests); Assert.False(link.Failed);
        Assert.DoesNotContain("\r", serial.Controls);
        if (!readyWithReply) Assert.Equal(2, serial.Controls.Count(c => c == "ATI\r"));
        else Assert.DoesNotContain("!", serial.Controls);
    }

    [Fact]
    public void Slow_first_reply_and_pending_reply_are_not_cut_off_by_a_shorter_timeout()
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { Delay = 150, Pending = true };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm(); int start = clock.Now;
        Assert.Equal(Fixture.Hex("62000C1F40"), link.ExchangeLogger([0x22, 0, 0x0C], 5));
        Assert.True(clock.Now - start >= 150); Assert.Single(serial.Requests); Assert.False(link.Failed);
    }

    [Fact]
    public void Longer_reassembled_reply_uses_its_exact_definition_length()
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { Reply = "62000C010203040506", Fragment = 2 };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        Assert.Equal(Fixture.Hex(serial.Reply), link.ExchangeLogger([0x22, 0, 0x0C], 9));
        Assert.Single(serial.Requests); Assert.False(link.Failed);
    }

    [Fact]
    public void Completed_negative_reply_retains_synchronization_for_the_next_query()
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { Reply = "7F2231" };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        Assert.Throws<NegativeResponseException>(() => link.ExchangeLogger([0x22, 0, 0x0C], 5));
        Assert.False(link.Failed); serial.Reply = "62000C1F40";
        Assert.Equal(Fixture.Hex(serial.Reply), link.ExchangeLogger([0x22, 0, 0x0C], 5)); Assert.Equal(2, serial.Requests.Count);
    }

    [Theory]
    [InlineData("62000D1F40")]
    [InlineData("62000C1F4000")]
    [InlineData("62000C1F")]
    [InlineData("7F2278")]
    [InlineData("")]
    public void Wrong_truncated_pending_only_or_missing_reply_faults_without_retry(string reply)
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { Reply = reply };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        Assert.Throws<TransportException>(() => link.ExchangeLogger([0x22, 0, 0x0C], 5));
        Assert.True(link.Failed); Assert.Single(serial.Requests);
        Assert.Throws<TransportException>(() => link.ExchangeLogger([0x22, 0, 0x0C], 5)); Assert.Single(serial.Requests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Missing_completion_prompt_or_bad_adapter_identity_cannot_resume_logging(bool missingPrompt, bool badIdentity)
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock) { MissingCompletionPrompt = missingPrompt, BadIdentity = badIdentity };
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        Assert.Throws<TransportException>(() => link.ExchangeLogger([0x22, 0, 0x0C], 5));
        Assert.True(link.Failed); Assert.Single(serial.Requests);
    }

    [Fact]
    public void Invalid_logger_metadata_and_programming_are_blocked_before_io()
    {
        var clock = new StepClock(); var serial = new ScriptedSerial(clock);
        using var link = new VLinkerReadTransport(serial, clock); link.Initialize(); serial.Arm();
        foreach (int length in new[] { 0, 3, 4096 }) Assert.Throws<TransportException>(() => link.ExchangeLogger([0x22, 0, 0x0C], length));
        Assert.Throws<TransportException>(() => link.ExchangeLogger([0x34, 0, 0], 5)); Assert.Empty(serial.Requests); Assert.Empty(serial.Controls);
    }

    private sealed class StepClock : IMonotonicClock
    {
        public int Now { get; private set; }
        public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Now++);
        public void Sleep(int milliseconds) => Now += milliseconds;
    }

    private sealed class ScriptedSerial(StepClock clock) : ISerialConnection
    {
        private readonly FakeSerial setup = new() { Segmented = true };
        private readonly Queue<byte> bytes = new();
        private readonly List<(int Due, string Text)> scheduled = [];
        private bool armed, payload, bufferedBang;
        public int Fragment { get; init; } = 16384;
        public int Delay { get; init; } = 10;
        public string Reply { get; set; } = "62000C1F40";
        public bool NaturalRace { get; init; }
        public bool ReadyWithReply { get; init; }
        public bool Pending { get; init; }
        public bool MissingCompletionPrompt { get; init; }
        public bool BadIdentity { get; init; }
        public List<string> Requests { get; } = [];
        public List<string> Controls { get; } = [];
        public void Arm() => armed = true;
        public bool IsOpen => setup.IsOpen;
        public int BaudRate { get => setup.BaudRate; set => setup.BaudRate = value; }
        private void Enqueue(string text) { foreach (byte b in Encoding.ASCII.GetBytes(text)) bytes.Enqueue(b); }
        private void Pump()
        {
            foreach (var item in scheduled.Where(x => x.Due <= clock.Now).ToArray()) { Enqueue(item.Text); scheduled.Remove(item); }
        }
        public int BytesToRead { get { Pump(); return setup.BytesToRead + bytes.Count; } }
        public int Read(byte[] buffer, int offset, int count)
        {
            Pump();
            if (setup.BytesToRead > 0) return setup.Read(buffer, offset, Math.Min(count, Fragment));
            int n = Math.Min(Math.Min(count, Fragment), bytes.Count);
            for (int i = 0; i < n; i++) buffer[offset + i] = bytes.Dequeue();
            if (n == 0) clock.Sleep(1);
            return n;
        }
        public int Write(byte[] data)
        {
            string text = Encoding.ASCII.GetString(data);
            if (!armed) return setup.Write(data);
            if (text.StartsWith("STPX ", StringComparison.Ordinal)) { payload = true; Enqueue("DATA>"); }
            else if (payload)
            {
                payload = false; Requests.Add(text.Trim());
                if (Pending) scheduled.Add((clock.Now + 2, "7F2278\r"));
                scheduled.Add((clock.Now + Delay, Reply + "\r" + (ReadyWithReply ? ">" : "")));
                if (!ReadyWithReply) scheduled.Add((clock.Now + Delay + 200, ">"));
            }
            else if (text == "!")
            {
                Controls.Add(text); scheduled.Clear();
                if (!MissingCompletionPrompt) { Enqueue(NaturalRace ? ">" : "STOPPED\r>"); bufferedBang = NaturalRace; }
            }
            else if (text == "ATI\r")
            {
                Controls.Add(text);
                if (bufferedBang) { bufferedBang = false; Enqueue("?\r>"); }
                else Enqueue(BadIdentity ? "wrong adapter\r>" : "ELM327 v2.3\r>");
            }
            else return setup.Write(data);
            return data.Length;
        }
        public void Flush() => setup.Flush();
        public void DiscardInput() { bytes.Clear(); scheduled.Clear(); setup.DiscardInput(); }
        public void Dispose() => setup.Dispose();
    }
}
