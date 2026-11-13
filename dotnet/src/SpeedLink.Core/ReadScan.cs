namespace SpeedLink.Core;

public sealed record ReadScanAttempt(int Address, int Count, bool Accepted, string Outcome);

public sealed record ReadScanResult(EcuIdentity Identity, ReadFamily Family, IReadOnlyList<ReadScanAttempt> Attempts)
{
    /// <summary>Largest block size accepted at address 0, or null when nothing at address 0 was accepted.</summary>
    public int? LargestBlockAtStart => Attempts.Where(a => a.Accepted && a.Address == 0).Select(a => (int?)a.Count).Max();
    public bool AnyAccepted => Attempts.Any(a => a.Accepted);
}

/// <summary>
/// Works out which memory reads an unfamiliar ECU accepts. Identifies the ECU, checks the engine is off,
/// unlocks the read session once (one security-key attempt), then sends a fixed list of small
/// ReadMemoryByAddress requests and records each reply. Only read requests are sent; nothing is written.
/// </summary>
public static class ReadScan
{
    /// <summary>Block sizes tried at address 0, smallest first.</summary>
    public static readonly int[] BlockSizes = [1, 16, 128, 255, 256, 512, 1023];

    /// <summary>Addresses tried with a 16-byte read, spread across a 1 MiB ROM.</summary>
    public static readonly int[] Addresses = [0x002000, 0x004000, 0x008000, 0x010000, 0x020000, 0x040000, 0x07FFF0, 0x080000, 0x0C0000, 0x0FFFF0];

    public static ReadScanResult Run(IReadTransport transport, ReadFamily unknownFamily = ReadFamily.Gen1, CancellationToken cancellation = default)
    {
        if (transport.Mode == "hardware")
        {
            if (transport.ReadAttempted)
                throw new TransportException("Reconnect and identify before another read.");
            transport.ReadAttempted = true;
        }
        transport.Diagnostics?.Record("scan.started");
        var identity = ReadWorkflow.Identify(transport);
        cancellation.ThrowIfCancellationRequested();
        ReadWorkflow.CheckEngineStopped(transport);
        cancellation.ThrowIfCancellationRequested();
        var protocol = ReadProtocol.For(identity, unknownFamily);
        transport.Diagnostics?.Record("read.protocol", new { family = protocol.Family.ToString(), protocol.Session, protocol.SeedLevel, protocol.MaximumBlock });
        ReadWorkflow.OpenReadSession(transport, protocol, cancellation);

        var plan = BlockSizes.Where(size => size <= protocol.MaximumBlock).Select(size => (Address: 0, Count: size))
            .Concat(Addresses.Select(address => (Address: address, Count: 16)));
        var attempts = new List<ReadScanAttempt>();
        foreach (var (address, count) in plan)
        {
            cancellation.ThrowIfCancellationRequested();
            ReadScanAttempt attempt;
            try
            {
                var response = transport.Exchange(protocol.ReadRequest(address, count));
                bool accepted = response.Length == count + 1 && response[0] == 0x63;
                attempt = new(address, count, accepted, accepted ? "OK" : $"unexpected reply ({response.Length} bytes)");
            }
            catch (NegativeResponseException error)
            {
                attempt = new(address, count, false, $"NRC {error.Code:X2}");
            }
            attempts.Add(attempt);
            transport.Diagnostics?.Record("scan.attempt", new { address = $"0x{address:X6}", count, attempt.Accepted, attempt.Outcome });
        }
        var result = new ReadScanResult(identity, protocol.Family, attempts);
        transport.Diagnostics?.Record("scan.completed", new { family = protocol.Family.ToString(), accepted = attempts.Count(a => a.Accepted), result.LargestBlockAtStart });
        return result;
    }
}
