namespace Sacred.Core.World.Stairs;

/// <summary>A treppe.bin coordinate with packed X/Y components and opaque high metadata.</summary>
public readonly record struct WorldStairsCoordinate(int X, int Y, byte Metadata)
{
    public const int HorizontalCoordinateBits = 13;
    public const int HorizontalCoordinateMask = (1 << HorizontalCoordinateBits) - 1;
    public const int MetadataShift = HorizontalCoordinateBits * 2;
    public const int MaximumMetadata = (1 << (32 - MetadataShift)) - 1;

    public static WorldStairsCoordinate FromPacked(uint packed) => new(
        (int)(packed & HorizontalCoordinateMask),
        (int)((packed >> HorizontalCoordinateBits) & HorizontalCoordinateMask),
        (byte)(packed >> MetadataShift));

    public uint ToPacked()
    {
        if ((uint)X > HorizontalCoordinateMask)
            throw new ArgumentOutOfRangeException(nameof(X));
        if ((uint)Y > HorizontalCoordinateMask)
            throw new ArgumentOutOfRangeException(nameof(Y));
        if (Metadata > MaximumMetadata)
            throw new ArgumentOutOfRangeException(nameof(Metadata));

        return (uint)X |
               ((uint)Y << HorizontalCoordinateBits) |
               ((uint)Metadata << MetadataShift);
    }
}
