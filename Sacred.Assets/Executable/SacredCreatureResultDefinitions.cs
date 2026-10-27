namespace Sacred.Assets.Executable;

/// <summary>Resource operand of CalcResults' on-foot default-speed branch.
/// Keeps the executable exception out of generic actor/file calculations.</summary>
public sealed class SacredCreatureResultDefinitions
{
    private readonly uint _slowResource;
    private SacredCreatureResultDefinitions(uint slowResource) => _slowResource = slowResource;
    public bool UsesSlowDefaultSpeeds(uint resource) => resource == _slowResource;

    public static SacredCreatureResultDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x55, 4, 0x83, 0x7A, 0x10];
        ReadOnlySpan<byte> tail = [0x75, 0x15, 0xB8, 0x64, 0, 0, 0, 0x66, 0xC7, 0x45, 0x40, 0x78, 0,
            0x66, 0x89, 0x45, 0x3C, 0x66, 0x89, 0x45, 0x3E, 0xEB, 0x12, 0x66, 0xC7, 0x45, 0x3C, 0x8C, 0,
            0x66, 0xC7, 0x45, 0x40, 0x82, 0, 0x66, 0xC7, 0x45, 0x3E, 0x78, 0];
        var found = -1;
        for (var search = 0; search <= image.Length - 48;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var at = search + relative;
            search = at + prefix.Length;
            if (at > image.Length - 48 || !image.AsSpan(at + 7, tail.Length).SequenceEqual(tail)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original actor default speeds.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original actor default-speed branch unavailable.");
        return new(unchecked((uint)(sbyte)image[found + 6]));
    }
}
