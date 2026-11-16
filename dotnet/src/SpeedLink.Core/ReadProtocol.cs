using System.Buffers.Binary;

namespace SpeedLink.Core;

public enum ReadFamily { Gen1, Gen2 }

/// <summary>Family-specific application read access. Never uploads code or enters programming mode.</summary>
public sealed record ReadProtocol(ReadFamily Family, byte Session, byte SeedLevel, byte KeyLevel, int MaximumBlock)
{
    public static ReadProtocol For(EcuIdentity identity, ReadFamily unknownFamily = ReadFamily.Gen1)
    {
        // Only the two observed identifiers select the Mazda 2 protocol automatically.
        // This is a read-access policy, not authorization to program this ECU.
        bool mazda2 = IsMazda2(identity.Calibration);
        var matches = RomInspector.KnownProfiles.Where(p => RomInspector.MatchesCalibrationId(p, identity.Calibration)).ToArray();
        if (matches.Length > 1 || mazda2 && matches.Length != 0) throw new TransportException("Ambiguous ECU calibration; no read was started.");
        if (mazda2) return new(ReadFamily.Gen2, 0x03, 0x03, 0x04, 512);
        var family = matches.Length == 1 ? matches[0].Family switch
        {
            "Gen1" => ReadFamily.Gen1,
            "Gen2" => ReadFamily.Gen2,
            _ => throw new TransportException("This ECU profile has no supported read protocol.")
        } : unknownFamily;
        return family switch
        {
            ReadFamily.Gen1 => new(family, 0x87, 0x01, 0x02, 3968),
            ReadFamily.Gen2 => new(family, 0x03, 0x03, 0x04, 1023),
            _ => throw new ArgumentOutOfRangeException(nameof(unknownFamily))
        };
    }

    public static bool IsMazda2(string? calibration) => calibration is "ZY87EC000" or "ZY87EC000ZYV5050";

    public byte[] ReadRequest(int address, int count)
    {
        if (address < 0 || count < 1 || count > MaximumBlock || (long)address + count > RomInspector.RomSize)
            throw new TransportException("Read request exceeds the supported ROM bounds.");
        byte[] request = new byte[Family == ReadFamily.Gen2 ? 8 : 7];
        request[0] = 0x23;
        int start = 1;
        if (Family == ReadFamily.Gen2) { request[1] = 0x24; start = 2; }
        BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(start, 4), address);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(start + 4, 2), (ushort)count);
        return request;
    }
}
