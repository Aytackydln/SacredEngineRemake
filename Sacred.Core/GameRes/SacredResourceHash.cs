namespace Sacred.Core.GameRes;

public static class SacredResourceHash
{
    private const uint Multiplier = 113;
    private const uint Modulus = 999_999_991;

    public static uint Compute(ReadOnlySpan<char> resourceKey)
    {
        uint hash = 0;

        foreach (var character in resourceKey)
        {
            var upperCharacter = char.ToUpperInvariant(character);
            hash = (uint)(((ulong)hash * Multiplier + upperCharacter) % Modulus);
        }

        return hash & 0x7FFF_FFFF;
    }
}
