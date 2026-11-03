namespace SpeedLink.Core;

public sealed record LoggerDisplayUpdate(LoggerFrame Frame, bool ScanComplete);

/// <summary>Bounded display-only handoff. A slow screen never blocks ECU polling or drops recorded frames.</summary>
public sealed class LoggerDisplayBuffer(int capacity = 256)
{
    private readonly object gate = new();
    private readonly Queue<LoggerDisplayUpdate> updates = new();
    private readonly int capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private long dropped;
    public long Dropped { get { lock (gate) return dropped; } }
    public void Publish(LoggerFrame frame, bool scanComplete)
    {
        lock (gate)
        {
            if (updates.Count == capacity) { updates.Dequeue(); dropped++; }
            updates.Enqueue(new(frame, scanComplete));
        }
    }
    public IReadOnlyList<LoggerDisplayUpdate> Drain()
    {
        lock (gate) { var batch = updates.ToArray(); updates.Clear(); return batch; }
    }
}
