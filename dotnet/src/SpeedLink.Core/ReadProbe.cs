namespace SpeedLink.Core;

public sealed record ReadProbeResult(EcuIdentity Identity, ReadFamily Family, bool SecurityKeyRequired, bool FullRomReadable, string? UpperReadError)
{
    public bool Passed => FullRomReadable;
}

/// <summary>
/// Checks that a full backup can start, without reading the ROM: identify, confirm the engine is off,
/// unlock the read session once and read two small samples at the start and end of the ROM.
/// Uses only the read-only request set; nothing is written to the ECU.
/// </summary>
public static class ReadProbe
{
    public const int SampleBytes = 16;

    public static ReadProbeResult Run(IReadTransport transport, ReadFamily unknownFamily = ReadFamily.Gen1, CancellationToken cancellation = default)
    {
        if (transport.Mode == "hardware")
        {
            if (transport.ReadAttempted)
                throw new TransportException("Reconnect and identify before another read.");
            transport.ReadAttempted = true;
        }
        transport.Diagnostics?.Record("probe.started");
        var identity = ReadWorkflow.Identify(transport);
        cancellation.ThrowIfCancellationRequested();
        ReadWorkflow.CheckEngineStopped(transport);
        cancellation.ThrowIfCancellationRequested();
        var protocol = ReadProtocol.For(identity, unknownFamily);
        transport.Diagnostics?.Record("read.protocol", new { family = protocol.Family.ToString(), protocol.Session, protocol.SeedLevel, protocol.MaximumBlock });
        bool keyed = ReadWorkflow.OpenReadSession(transport, protocol, cancellation);
        cancellation.ThrowIfCancellationRequested();
        ReadSample(transport, protocol, 0);
        string? upperError = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            ReadSample(transport, protocol, RomInspector.RomSize - SampleBytes);
        }
        catch (NegativeResponseException error)
        {
            upperError = error.Message;
        }
        transport.Diagnostics?.Record("probe.completed", new { family = protocol.Family.ToString(), securityKeyRequired = keyed, fullRomReadable = upperError is null });
        return new(identity, protocol.Family, keyed, upperError is null, upperError);
    }

    private static void ReadSample(IReadTransport transport, ReadProtocol protocol, int address)
    {
        var response = transport.Exchange(protocol.ReadRequest(address, SampleBytes));
        if (response.Length != SampleBytes + 1 || response[0] != 0x63)
            throw new TransportException($"Short or malformed read at 0x{address:X6}; expected {SampleBytes} bytes.");
    }
}
