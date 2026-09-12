using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>One linked floor-overlay record stored in <c>Floor.pak</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 16)]
public readonly record struct FloorOverlayRecord(uint Unknown0, uint TileOrBlendRef, uint Unknown8, uint NextFloorId)
{
    private const uint PrimaryTileMask = 0x1FFFF;
    private const int SecondaryTileShift = 17;
    private const uint SecondaryTileMask = 0x7FFF;

    /// <summary>Native sTransitionStatic.self. See PayloadInstanceId.</summary>
    [FieldOffset(0x00)] public readonly uint Unknown0 = Unknown0;
    /// <summary>Packed primary and secondary tile references.</summary>
    [FieldOffset(0x04)] public readonly uint TileOrBlendRef = TileOrBlendRef;
    /// <summary>Native sTransitionStatic.flags. Individual bits remain unresolved.</summary>
    [FieldOffset(0x08)] public readonly uint Unknown8 = Unknown8;
    /// <summary>Identifier of the next overlay in this tile's linked list.</summary>
    [FieldOffset(0x0C)] public readonly uint NextFloorId = NextFloorId;

    /// <summary>Native self identifier at offset 0x00.</summary>
    [FieldOffset(0x00)] public readonly uint PayloadInstanceId = Unknown0;
    /// <summary>Native flags word at offset 0x08; no bit meanings are inferred from Static.pak.</summary>
    [FieldOffset(0x08)] public readonly uint Flags = Unknown8;

    public uint PrimaryTileId => GetPrimaryTileId(TileOrBlendRef);
    public uint SecondaryTileId => GetSecondaryTileId(TileOrBlendRef);

    private static uint GetPrimaryTileId(uint tileOrBlendRef) => tileOrBlendRef & PrimaryTileMask;

    private static uint GetSecondaryTileId(uint tileOrBlendRef) =>
        (tileOrBlendRef >> SecondaryTileShift) & SecondaryTileMask;
}
