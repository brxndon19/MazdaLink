using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class LoggerDisplayBufferTests
{
    private static LoggerFrame Frame(long number) => new(number, DateTimeOffset.UtcNow, TimeSpan.Zero,
        new Dictionary<string, LoggerReading>(), []);

    [Fact]
    public void Slow_display_cannot_block_acquisition_and_keeps_newest_updates_bounded()
    {
        var buffer = new LoggerDisplayBuffer(3);
        for (int n = 1; n <= 1000; n++) buffer.Publish(Frame(n), n % 2 == 0);
        Assert.Equal(997, buffer.Dropped);
        var batch = buffer.Drain();
        Assert.Equal(new long[] { 998, 999, 1000 }, batch.Select(x => x.Frame.Scan));
        Assert.True(batch[^1].ScanComplete); Assert.Empty(buffer.Drain());
        buffer.Publish(Frame(1001), false); Assert.Equal(1001, Assert.Single(buffer.Drain()).Frame.Scan);
    }

    [Fact]
    public async Task Display_and_acquisition_can_run_concurrently_without_changing_recorded_frames()
    {
        var buffer = new LoggerDisplayBuffer(16); var recorded = new List<long>();
        var producer = Task.Run(() => { for (long n = 1; n <= 10000; n++) { recorded.Add(n); buffer.Publish(Frame(n), true); } });
        var shown = new List<long>();
        while (!producer.IsCompleted) { shown.AddRange(buffer.Drain().Select(x => x.Frame.Scan)); await Task.Yield(); }
        await producer; shown.AddRange(buffer.Drain().Select(x => x.Frame.Scan));
        Assert.Equal(10000, recorded.Count); Assert.Equal(10000, shown[^1]);
        Assert.True(shown.SequenceEqual(shown.Order().Distinct()));
    }
}
