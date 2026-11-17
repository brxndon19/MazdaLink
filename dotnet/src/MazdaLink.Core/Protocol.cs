using System.Buffers.Binary;
using System.Text;

namespace MazdaLink.Core;

public class TransportException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class NegativeResponseException(byte service, byte code) : TransportException($"ECU refused service {service:X2}: NRC {code:X2}. No retry was attempted.")
{
    public byte Service { get; } = service;
    public byte Code { get; } = code;
}
public interface IDiagnosticSink { void Record(string name, object? value = null); }
public interface IReadTransport : IDisposable
{
    byte[] Exchange(byte[] request);
}
public static class TargetPolicy
{
    public const string SoftwareId = "ZY87EC000ZYV5050";
    public const int RomSize = 0x100000, BlockSize = 512;
    public static void RequireIdentity(string? identity)
    {
        // A reported identity is required for consistency/provenance, not as a calibration whitelist.
        if (string.IsNullOrEmpty(identity) || identity.Length > 64 || identity.Any(c => c < 0x21 || c > 0x7E))
            throw new InvalidDataException("The ECU reported a missing or malformed calibration identity. No unlock or ROM read was attempted.");
    }
    public static void RequireSoftware(ReadOnlySpan<byte> rom)
    {
        if (rom.Length != RomSize || !rom.Slice(0xC0000, 8).SequenceEqual("ZY87EC0W"u8) || !rom.Slice(Zy87Integrity.IdentityOffset, SoftwareId.Length).SequenceEqual(Encoding.ASCII.GetBytes(SoftwareId)))
            throw new InvalidDataException($"The ROM must be a complete 1 MiB {SoftwareId} image.");
    }
    public static string Identify(IReadTransport transport)
    {
        var reply = transport.Exchange([9, 4]);
        if (reply.Length < 4 || !reply.AsSpan(0, 3).SequenceEqual(new byte[] { 0x49, 4, 1 }))
            throw new TransportException("The ECU returned an invalid calibration identity.");
        string identity = Encoding.ASCII.GetString(reply.AsSpan(3).TrimEnd((byte)0));
        RequireIdentity(identity);
        return identity;
    }
}
public static class DiagnosticProtocol
{
    public static string Describe(ReadOnlySpan<byte> request) => request[0] switch { 9 => "identity", 1 => "engine RPM", 0x10 => "read session", 0x27 => "security access", 0x23 => "ROM read", _ => "blocked" };
    public static void CheckReadOnly(ReadOnlySpan<byte> request, int maxRead = 512)
    {
        bool allowed = request.SequenceEqual(new byte[] { 9, 4 }) || request.SequenceEqual(new byte[] { 1, 0x0C }) ||
            request.SequenceEqual(new byte[] { 0x10, 3 }) || request.SequenceEqual(new byte[] { 0x27, 3 }) ||
            request.Length == 5 && request[0] == 0x27 && request[1] == 4;
        if (request.Length == 8 && request[0] == 0x23 && request[1] == 0x24)
        {
            uint address = BinaryPrimitives.ReadUInt32BigEndian(request.Slice(2, 4));
            int count = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(6, 2));
            allowed |= count > 0 && count <= Math.Min(maxRead, 512) && (ulong)address + (uint)count <= TargetPolicy.RomSize;
        }
        if (!allowed) throw new TransportException("Request blocked. MazdaLink has no enabled ECU erase/write/reset or other-vehicle protocol.");
    }
    public static byte[] ReadRequest(int address, int count)
    {
        if (address < 0 || count < 1 || count > 512 || (long)address + count > TargetPolicy.RomSize)
            throw new ArgumentOutOfRangeException(nameof(address));
        byte[] request = [0x23, 0x24, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(2), address);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)count);
        return request;
    }
    public static byte[] FinalResponse(ReadOnlySpan<byte> request, IReadOnlyList<byte[]> packets)
    {
        byte[]? result = null;
        foreach (var packet in packets)
        {
            if (packet.Length == 0) throw new TransportException("Empty ECU response.");
            if (packet[0] == 0x7F)
            {
                if (packet.Length != 3 || packet[1] != request[0] || result is not null) throw new TransportException("Malformed negative response.");
                if (packet[2] == 0x78) continue;
                throw new NegativeResponseException(packet[1], packet[2]);
            }
            if (result is not null || packet[0] != request[0] + 0x40) throw new TransportException("Unexpected ECU response.");
            result = packet;
        }
        return result ?? throw new TransportException("Response remained pending. No request was resent.");
    }
}
