namespace Sacred.Core.GameRes;

public static class SacredResourceHash
{
    private const int Multiplier = 113;
    private const int Modulus = 999_999_991;

    public static uint Compute(ReadOnlySpan<char> resourceKey)
    {
        int hash = 0;

        foreach (var character in resourceKey)
        {
            var upperCharacter = char.ToUpperInvariant(character);
            // Sacred wraps the multiplication to signed 32 bits before signed IDIV.
            hash = unchecked(hash * Multiplier + upperCharacter) % Modulus;
        }

        return (uint)hash & 0x7FFF_FFFF;
    }
}
