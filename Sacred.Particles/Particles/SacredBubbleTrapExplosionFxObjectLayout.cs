using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x2144)]
public struct SacredBubbleTrapExplosionFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x9C)] public uint LightStrength;
    [FieldOffset(0xA4)] public float GroundHeightOffset;
    [FieldOffset(0xC0)] public SacredFxSurfaceVertexLayout RingVertex0;
    [FieldOffset(0xE0)] public SacredFxSurfaceVertexLayout RingVertex1;
    [FieldOffset(0x100)] public SacredFxSurfaceVertexLayout RingVertex2;
    [FieldOffset(0x120)] public SacredFxSurfaceVertexLayout RingVertex3;
    [FieldOffset(0x20A0)] public SacredBubbleTrapExplosionFxStateLayout State;
    [FieldOffset(0x2140)] public uint RingTextureHandle;
}
