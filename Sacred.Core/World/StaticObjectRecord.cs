using System.Runtime.InteropServices;

namespace Sacred.Core.World;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly record struct StaticObjectRecord
{
    public const int SerializedSize = 0x40;

    [FieldOffset(0x00)]
    public readonly uint PayloadInstanceId;

    [FieldOffset(0x04)]
    public readonly uint TypeId;

    [FieldOffset(0x08)]
    public readonly uint Flags;

    [FieldOffset(0x0C)]
    public readonly ushort SectorId;

    [FieldOffset(0x0E)]
    public readonly int ProjectedX;

    [FieldOffset(0x12)]
    public readonly int ProjectedY;

    [FieldOffset(0x1F)]
    public readonly uint NextStaticId;

    [FieldOffset(0x2B)]
    public readonly short SurfaceRenderLayer;

    [FieldOffset(0x2E)]
    public readonly byte SpriteParam2E;

    [FieldOffset(0x2F)]
    public readonly byte SpriteParam2F;

    [FieldOffset(0x30)]
    public readonly byte OrientationOrFrame;

    [FieldOffset(0x33)]
    public readonly byte ElevationTier;
}
