namespace Sacred.Core.GameRes;

public static class SacredResourceHash
{
    private const int Multiplier = 113;
    private const int Modulus = 999_999_991;

    /// <summary>Gold 0x80EAA0 with the default C locale: signed source bytes, ASCII-only toupper,
    /// signed overflow before IDIV, and a final sign-bit mask. Used for serialized script function references.</summary>
    public static uint ComputeDefaultCLocale(ReadOnlySpan<byte> name)
    {
        var hash = 0;
        foreach (var value in name)
        {
            if (value == 0) break;
            var upper = value is >= (byte)'a' and <= (byte)'z' ? value - 32 : unchecked((sbyte)value);
            hash = unchecked(hash * Multiplier + upper) % Modulus;
        }
        return (uint)hash & 0x7FFF_FFFF;
    }

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
