using MazdaLink.Core;

namespace MazdaLink.Tests;

public sealed class BenchTimingTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(double milliseconds) => Ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
    [Fact]
    public void RoundedWindowsPollingDoesNotStretchTwentySecondsToTwentyFive()
    {
        var clock = new Clock(); int polls = 0;
        BenchEntryWindow.Wait(TimeSpan.FromSeconds(20), requested =>
        {
            Assert.InRange(requested, 1, 25); polls++;
            clock.Advance(Math.Ceiling(requested / 15.625) * 15.625);
        }, clock: clock);
        Assert.InRange(clock.GetElapsedTime(0).TotalMilliseconds, 20000, 20016);
        Assert.InRange(polls, 600, 700);
    }
    [Fact]
    public void SlowPollingAndStartLatencyConsumeTheSameDeadline()
    {
        var clock = new Clock(); clock.Advance(3000); int polls = 0;
        BenchEntryWindow.Wait(TimeSpan.FromSeconds(20), _ => { polls++; clock.Advance(500); },
            clock: clock, startedTimestamp: 0);
        Assert.Equal(34, polls);
        Assert.Equal(TimeSpan.FromSeconds(20), clock.GetElapsedTime(0));
    }
    [Fact]
    public void CancellationDuringPollingStopsBeforeAnotherPoll()
    {
        var clock = new Clock(); int polls = 0; using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => BenchEntryWindow.Wait(TimeSpan.FromSeconds(20), _ =>
        { polls++; clock.Advance(25); cancel.Cancel(); }, cancel.Token, clock));
        Assert.Equal(1, polls);
    }
    [Fact]
    public void AnExpiredDeadlineDoesNotPollOrAddAnotherWindow()
    {
        var clock = new Clock(); clock.Advance(21000);
        BenchEntryWindow.Wait(TimeSpan.FromSeconds(20), _ => throw new Exception("Must not poll"), clock: clock, startedTimestamp: 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchEntryWindow.Wait(TimeSpan.FromMilliseconds(-1), _ => { }));
    }
}
