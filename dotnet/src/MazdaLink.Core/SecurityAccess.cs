// Adapted from SpeedLink, GPL-3.0-or-later.
namespace MazdaLink.Core;

public static class SecurityAccess
{
    public static byte[] SeedKey(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 3)
            throw new InvalidDataException("The read-access seed must contain exactly three bytes.");
        uint state = 0xC541A9;
        foreach (var value in seed.ToArray().Concat("MazdA"u8.ToArray()))
        {
            int current = value;
            for (int bit = 0; bit < 8; bit++)
            {
                state = ((state >> 1) ^ (((state ^ (uint)current) & 1) != 0 ? 0x909028u : 0u)) & 0xFFFFFF;
                current >>= 1;
            }
        }
        return [(byte)((state >> 4) & 255), (byte)(((state >> 8) & 240) | ((state >> 20) & 15)), (byte)(((state << 4) & 240) | ((state >> 16) & 15))];
    }
}
