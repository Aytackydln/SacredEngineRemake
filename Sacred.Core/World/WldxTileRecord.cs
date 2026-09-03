using System.Runtime.InteropServices;
using Sacred.Core.World.Pathing;

namespace Sacred.Core.World;

[Flags]
public enum WldxTileFlags : byte
{
    None = 0,
    /// <summary>First exact authored movement-blocking value.</summary>
    MovementBlockerA = 0x01,
    /// <summary>Second exact authored movement-blocking value.</summary>
    MovementBlockerB = 0x02,
    Shadowable = 0x04,
    CanFadeModelsBehind = 0x08,

    Entrance = MovementBlockerA | CanFadeModelsBehind,
    ExteriorEntranceBoundary = MovementBlockerB | CanFadeModelsBehind,
}

/// <summary>
/// Packed terrain-surface value stored in the high nibble of a WLDX tile's byte 0x1F.
/// Sacred.exe selects terrain behavior, materials, and footsteps from exact composite values.
/// </summary>
[Flags]
public enum WldxTerrainSurface : byte
{
    Default = 0x00,
    Surface10 = 0x10,
    Surface20 = 0x20,
    Surface30 = 0x30,
    Surface40 = 0x40,
    Surface50 = 0x50,
    Surface60 = 0x60,
    Surface70 = 0x70,
    /// <summary>
    /// Bit shared by both authored liquid surface values. It is not sufficient
    /// by itself: 0xB0 and higher combinations are non-liquid material values.
    /// </summary>
    LiquidFamilyMarker = 0x80,
    LiquidA = LiquidFamilyMarker | Surface10,
    LiquidB = LiquidFamilyMarker | Surface20,
    SurfaceB0 = 0xB0,
    SurfaceC0 = 0xC0,
    SurfaceD0 = 0xD0,
    SurfaceE0 = 0xE0,
    SurfaceF0 = 0xF0,
}

/// <summary>Packed low-nibble behavior flags and high-nibble terrain-surface value.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly record struct WldxTileProperties
{
    public const byte TileFlagsMask = 0x0F;
    public const byte TerrainSurfaceMask = 0xF0;

    private readonly byte _value;

    public WldxTileFlags TileFlags => (WldxTileFlags)(_value & TileFlagsMask);
    public WldxTerrainSurface TerrainSurface => (WldxTerrainSurface)(_value & TerrainSurfaceMask);

    /// <summary>
    /// Whether the complete low-nibble value is one of Sacred's two movement blockers.
    /// Door composites 0x09 and 0x0A reuse these bits but remain traversable.
    /// </summary>
    public bool BlocksMovement =>
        TileFlags is WldxTileFlags.MovementBlockerA or WldxTileFlags.MovementBlockerB;
    public bool IsShadowable => TileFlags.HasFlag(WldxTileFlags.Shadowable);
    public bool CanFadeModelsBehind => TileFlags.HasFlag(WldxTileFlags.CanFadeModelsBehind);
    public bool IsEntrance => TileFlags.HasFlag(WldxTileFlags.Entrance);
    public bool IsEntranceBoundary =>
        TileFlags is WldxTileFlags.Entrance or WldxTileFlags.ExteriorEntranceBoundary;
    public bool IsLiquid => TerrainSurface is WldxTerrainSurface.LiquidA or WldxTerrainSurface.LiquidB;

    // Sacred.exe keeps the low nibble when selecting the floor-chain insertion point.
    public byte FloorInsertionDepth => (byte)TileFlags;

    public override string ToString() => $"0x{_value:X2}";
}

/// <summary>One 0x20-byte outdoor or indoor tile record in a decompressed WLDX payload.</summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly record struct WldxTileRecord
{
    public const int Size = 0x20;

    /// <summary>Ground-tile definition identifier.</summary>
    [FieldOffset(0x00)] public readonly uint GroundTileId;
    /// <summary>Head identifier of the linked Static.pak object chain.</summary>
    [FieldOffset(0x04)] public readonly uint StaticChainHeadId;
    /// <summary>Head identifier of the linked Floor.pak overlay chain.</summary>
    [FieldOffset(0x0C)] public readonly uint FloorChainHeadId;
    /// <summary>
    /// Signed visual Y offset at the left/south-west terrain vertex. On liquid tiles,
    /// Sacred.exe also converts its negative magnitude to per-corner liquid opacity.
    /// </summary>
    [FieldOffset(0x10)] public readonly sbyte VisualElevationLeft;
    /// <summary>Signed visual Y offset at the top/north-west terrain vertex.</summary>
    [FieldOffset(0x11)] public readonly sbyte VisualElevationTop;
    /// <summary>Signed visual Y offset at the right/north-east terrain vertex.</summary>
    [FieldOffset(0x12)] public readonly sbyte VisualElevationRight;
    /// <summary>Signed visual Y offset at the bottom/south-east terrain vertex.</summary>
    [FieldOffset(0x13)] public readonly sbyte VisualElevationBottom;
    // Verified by exact corner continuity across adjacent WLDX tiles. Values are
    // authored in steps of 0x14, with 0xFF representing an unoccluded vertex.
    /// <summary>Baked terrain brightness at the south-west corner.</summary>
    [FieldOffset(0x14)] public readonly byte BakedBrightnessSouthWest;
    /// <summary>Baked terrain brightness at the north-west corner.</summary>
    [FieldOffset(0x15)] public readonly byte BakedBrightnessNorthWest;
    /// <summary>Baked terrain brightness at the north-east corner.</summary>
    [FieldOffset(0x16)] public readonly byte BakedBrightnessNorthEast;
    /// <summary>Baked terrain brightness at the south-east corner.</summary>
    [FieldOffset(0x17)] public readonly byte BakedBrightnessSouthEast;
    // Verified by exact corner continuity across adjacent elevated WLDX tiles.
    /// <summary>Signed terrain elevation at the south-west corner.</summary>
    [FieldOffset(0x18)] public readonly sbyte ElevationSouthWest;
    /// <summary>Signed terrain elevation at the north-west corner.</summary>
    [FieldOffset(0x19)] public readonly sbyte ElevationNorthWest;
    /// <summary>Signed terrain elevation at the north-east corner.</summary>
    [FieldOffset(0x1A)] public readonly sbyte ElevationNorthEast;
    /// <summary>Signed terrain elevation at the south-east corner.</summary>
    [FieldOffset(0x1B)] public readonly sbyte ElevationSouthEast;
    /// <summary>
    /// Signed X delta to the authored anchor tile of a multi-tile indoor surface.
    /// Adding this value to the tile's world X resolves the shared anchor.
    /// </summary>
    [FieldOffset(0x1C)] public readonly sbyte IndoorAnchorDeltaX;
    /// <summary>
    /// Signed Y delta to the authored anchor tile of a multi-tile indoor surface.
    /// Adding this value to the tile's world Y resolves the shared anchor.
    /// </summary>
    [FieldOffset(0x1D)] public readonly sbyte IndoorAnchorDeltaY;
    /// <summary>
    /// Authored per-tile world flags, including indoor areas, towns, triggers,
    /// and runtime movement blocking.
    /// </summary>
    [FieldOffset(0x1E)] public readonly WorldPathFlags PathFlags;
    /// <summary>
    /// Packed tile-behavior flags in the low nibble and terrain-surface value in the high nibble.
    /// </summary>
    [FieldOffset(0x1F)] public readonly WldxTileProperties Properties;

    public static WldxTileRecord FromBytes(ReadOnlySpan<byte> data)
    {
        // Cast the first record-sized span through the explicit FieldOffset layout.
        return MemoryMarshal.Cast<byte, WldxTileRecord>(data)[0];
    }

    /// <summary>
    /// Whether a sparse indoor-grid cell contains an authored record. The complete serialized
    /// record is checked so bytes whose purpose is not mapped yet cannot be discarded.
    /// </summary>
    public static bool HasAuthoredData(ReadOnlySpan<byte> data) =>
        data[..Size].IndexOfAnyExcept((byte)0) >= 0;
}
