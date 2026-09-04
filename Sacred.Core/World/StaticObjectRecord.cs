using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Known rendering and surface-selection bits in a Static.pak object record.</summary>
[Flags]
public enum StaticObjectFlags : uint
{
    None = 0,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000001 = 0x0000_0001,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000002 = 0x0000_0002,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000004 = 0x0000_0004,
    AlternateSurface = 0x0000_0008,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000010 = 0x0000_0010,
    RearLayerBackground = 0x0000_0020,
    NightOnly = 0x0000_0040,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000080 = 0x0000_0080,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000100 = 0x0000_0100,
    /// <summary>Unresolved flag bit used by Sacred.exe's normal-render exclusion test.</summary>
    Byte00000200 = 0x0000_0200,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000400 = 0x0000_0400,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00000800 = 0x0000_0800,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00001000 = 0x0000_1000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00002000 = 0x0000_2000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00004000 = 0x0000_4000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00008000 = 0x0000_8000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00010000 = 0x0001_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00020000 = 0x0002_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00040000 = 0x0004_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00080000 = 0x0008_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00100000 = 0x0010_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00200000 = 0x0020_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00400000 = 0x0040_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte00800000 = 0x0080_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte01000000 = 0x0100_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte02000000 = 0x0200_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte04000000 = 0x0400_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte08000000 = 0x0800_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte10000000 = 0x1000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte20000000 = 0x2000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte40000000 = 0x4000_0000,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal value.</summary>
    Byte80000000 = 0x8000_0000,

    /// <summary>Composite mask of objects excluded from the normal static-sprite pass.</summary>
    NormalRenderExclusionMask = Byte00000010 | Byte00000080 | Byte00000200,
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
