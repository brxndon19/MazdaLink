namespace SpeedLink.Core;

/// <summary>Bounded read-only standard identifiers whose payloads have independently verified enhanced aliases.</summary>
public static class Mode01LoggerProtocol
{
    private static readonly Dictionary<byte, int> widths = new() { [0x0C] = 2, [0x43] = 2, [0x0E] = 1, [0x10] = 2, [0x23] = 2 };
    private static readonly HashSet<string> verified = new(StringComparer.Ordinal)
    { "L33XEH000", "L35HEH000", "L38KEH000", "L39SED000", "L39SEE000", "L39SEF000", "L3M6EG000", "L3M7EF000", "L3P4EM000" };
    internal static bool HasVerifiedAliases(LoggerDefinition definition) => definition.SoftwareIds.Count == 1 && verified.Contains(definition.SoftwareIds[0]);
    internal static bool IsAlias(byte[] request, int responseLength) => request.Length == 3 && request[0] == 0x22 && request[1] == 0 &&
        widths.TryGetValue(request[2], out int width) && responseLength == width + 3;
    public static bool IsAllowed(ReadOnlySpan<byte> request)
    {
        if (request.Length is < 2 or > 6 || request[0] != 1) return false;
        Span<bool> seen = stackalloc bool[256];
        seen.Clear();
        foreach (byte id in request[1..]) { if (!widths.ContainsKey(id) || seen[id]) return false; seen[id] = true; }
        return true;
    }
    public static int ResponseLength(ReadOnlySpan<byte> request)
    {
        if (!IsAllowed(request)) throw new TransportException("Unsupported standard logger request.");
        int size = 1;
        foreach (byte id in request[1..]) size += 1 + widths[id];
        return size;
    }
    public static IReadOnlyDictionary<byte, byte[]> Split(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply)
    {
        if (reply.Length != ResponseLength(request) || reply[0] != 0x41) throw new TransportException("Wrong standard logger response length or service.");
        var result = new Dictionary<byte, byte[]>(); int at = 1;
        foreach (byte id in request[1..])
        {
            if (reply[at++] != id) throw new TransportException("Wrong standard logger identifier or order.");
            int size = widths[id]; result.Add(id, reply.Slice(at, size).ToArray()); at += size;
        }
        return result;
    }
}
