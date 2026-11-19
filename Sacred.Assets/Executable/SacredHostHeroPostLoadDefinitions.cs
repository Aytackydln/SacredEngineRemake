namespace Sacred.Assets.Executable;

/// <summary>Original C0 class-specific extra action at Gold 605B04. Its resource
/// operand comes from the executable; unverified class work remains guarded.</summary>
public sealed class SacredHostHeroPostLoadDefinitions
{
    private SacredHostHeroPostLoadDefinitions(uint extraActionResource) => ExtraActionResource = extraActionResource;
    public uint ExtraActionResource { get; }
    public static SacredHostHeroPostLoadDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> suffix = [0x75, 0x0a, 0x8b, 0x06, 0x53, 0x6a, 0x15, 0x8b, 0xce, 0xff, 0x50, 0x24, 0x8b, 0x3e, 0x53, 0x6a, 0x24];
        ReadOnlySpan<byte> prefix = [0x83, 0x7e, 0x10];
        var found = -1;
        for (var i = 0; i <= image.Length - suffix.Length - 4; i++)
        {
            if (!image.Slice(i, 3).SequenceEqual(prefix) || !image.Slice(i + 4, suffix.Length).SequenceEqual(suffix)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original C0 hero class operand.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Original C0 hero class operand unavailable.");
        return new(image[found + 3]);
    }
}
