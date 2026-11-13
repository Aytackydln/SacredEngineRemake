namespace Sacred.Assets.Executable;

/// <summary>Original CalcResults resource operands for the final speed floor
/// and spell/art regeneration sharing. These exceptions belong to the image.</summary>
public sealed class SacredCreatureResultFinalizationDefinitions
{
    private readonly uint _slowResource;
    private readonly uint _sharedRegenerationResource;
    private SacredCreatureResultFinalizationDefinitions(uint slow, uint shared) => (_slowResource, _sharedRegenerationResource) = (slow, shared);
    public bool UsesSlowSpeedFloor(uint resource) => resource == _slowResource;
    public bool SharesCombatArtRegeneration(uint resource) => resource == _sharedRegenerationResource;

    public static SacredCreatureResultFinalizationDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x45, 4, 0x8B, 0x48, 0x10, 0x83, 0xF9];
        ReadOnlySpan<byte> floors = [0xB8, 0x50, 0, 0, 0, 0x66, 0x39, 0x45, 0x3C, 0x7D, 4, 0x66, 0x89, 0x45, 0x3C,
            0x66, 0x39, 0x45, 0x40, 0x7D, 4, 0x66, 0x89, 0x45, 0x40, 0x66, 0x39, 0x45, 0x3E, 0x7D, 4, 0x66, 0x89, 0x45, 0x3E];
        ReadOnlySpan<byte> copy = [0x75, 0x0C, 0x8B, 0x8D, 0xF2, 0, 0, 0, 0x89, 0x8D, 0xEE, 0, 0, 0];
        var found = -1;
        for (var search = 0; search <= image.Length - 72;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var at = search + relative;
            search = at + prefix.Length;
            if (at > image.Length - 72 || !image.AsSpan(at + 15, floors.Length).SequenceEqual(floors) ||
                !image.AsSpan(at + 58, copy.Length).SequenceEqual(copy)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original result-finalization branches.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original result-finalization branches unavailable.");
        return new(unchecked((uint)(sbyte)image[found + 8]), unchecked((uint)(sbyte)image[found + 57]));
    }
}
