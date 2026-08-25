using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Known rendering and surface-selection bits in a Static.pak object record.</summary>
[Flags]
public enum StaticObjectFlags : uint
{
    None = 0,
    AlternateSurface = 0x0000_0008,
    RearLayerBackground = 0x0000_0020,
    NightOnly = 0x0000_0040,

    /// <summary>Composite mask of objects excluded from the normal static-sprite pass.</summary>
    NormalRenderExclusionMask = 0x0000_0290,
}

/// <summary>One linked static-world object record stored in <c>Static.pak</c>.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct StaticObjectRecord
{
    public const int SerializedSize = 0x40;

    /// <summary>Instance identifier stored in the payload.</summary>
    [FieldOffset(0x00)]
    public readonly uint PayloadInstanceId;

    /// <summary>Items.pak type identifier selecting the object's visual definition.</summary>
    [FieldOffset(0x04)]
    public readonly uint TypeId;

    /// <summary>Rendering and surface-selection flags.</summary>
    [FieldOffset(0x08)]
    public readonly StaticObjectFlags Flags;

    /// <summary>Owning sector identifier.</summary>
    [FieldOffset(0x0C)]
    public readonly ushort SectorId;

    /// <summary>Projected world X coordinate.</summary>
    [FieldOffset(0x0E)]
    public readonly int ProjectedX;

    /// <summary>Projected world Y coordinate.</summary>
    [FieldOffset(0x12)]
    public readonly int ProjectedY;

    /// <summary>Identifier of the next static object in the tile's linked list.</summary>
    [FieldOffset(0x1F)]
    public readonly uint NextStaticId;

    /// <summary>Signed layer controlling placement above or below the base surface.</summary>
    [FieldOffset(0x2B)]
    public readonly short SurfaceRenderLayer;

    /// <summary>First texture-region or animation selector byte.</summary>
    [FieldOffset(0x2E)]
    public readonly byte SpriteParam2E;

    /// <summary>Second texture-region or animation selector byte.</summary>
    [FieldOffset(0x2F)]
    public readonly byte SpriteParam2F;

    /// <summary>Sprite orientation or initial animation frame.</summary>
    [FieldOffset(0x30)]
    public readonly byte OrientationOrFrame;

    /// <summary>Duration of one animation frame in game ticks.</summary>
    [FieldOffset(0x31)]
    public readonly byte AnimationFrameDurationTicks;

    /// <summary>Number of frames in the object's sprite animation.</summary>
    [FieldOffset(0x32)]
    public readonly byte AnimationFrameCount;

    /// <summary>Authored elevation tier used to place the sprite.</summary>
    [FieldOffset(0x33)]
    public readonly byte ElevationTier;
}
