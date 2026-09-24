using System.Numerics;

namespace Sacred.Core.World.Sector;

public readonly record struct StaticWorldObject(
    uint StaticId,
    uint TypeId,
    StaticObjectFlags Flags,
    ushort SectorId,
    int ProjectedX,
    int ProjectedY,
    uint NextStaticId,
    short SurfaceRenderLayer,
    byte HeightLevelId,
    byte SpriteParam2E,
    byte SpriteParam2F,
    byte OrientationOrFrame,
    byte AnimationFrameDurationTicks,
    byte AnimationFrameCount,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder)
{
    /// <summary>Full tile-space position from a compiled script's world-coordinate operand.</summary>
    public Vector2? PreciseWorldPosition { get; init; }

    /// <summary>Owning building anchor from the outdoor WLDX tile's 0x1C/0x1D offsets.</summary>
    public (int X, int Y)? IndoorAnchor { get; init; }

    public byte MiniObjectSourceXOrAtlasColumns => SpriteParam2E;
    public byte MiniObjectSourceYOrAtlasRows => SpriteParam2F;
    public byte MiniObjectSourceSize => OrientationOrFrame;
    public byte MiniObjectFrameDurationTicks => AnimationFrameDurationTicks;
    public byte MiniObjectFrameCount => AnimationFrameCount;
    public bool UsesAlternateSurface => Flags.HasFlag(StaticObjectFlags.AlternateSurface);
    public bool IsExcludedFromNormalRender =>
        Flags.HasFlag(StaticObjectFlags.Byte00000010) ||
        Flags.HasFlag(StaticObjectFlags.Byte00000080) ||
        Flags.HasFlag(StaticObjectFlags.Byte00000200);
}
