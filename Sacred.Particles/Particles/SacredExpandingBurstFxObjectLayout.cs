using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Common object prefix, including all three resources; Ring of Poison allocation ends at +0x2538.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x2538)]
public struct SacredExpandingBurstFxObjectLayout
{
    [FieldOffset(8)] public Vector3 Position;
    [FieldOffset(0x68)] public uint ParticleVectorBegin;
    [FieldOffset(0x6C)] public uint ParticleVectorEnd;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x98)] public uint RenderRadius;
    [FieldOffset(0x9C)] public uint LightStrength;
    [FieldOffset(0xA4)] public float GroundHeightOffset;
    [FieldOffset(0xC0)] public SacredFxSurfaceVertexLayout RingVertex0;
    [FieldOffset(0xE0)] public SacredFxSurfaceVertexLayout RingVertex1;
    [FieldOffset(0x100)] public SacredFxSurfaceVertexLayout RingVertex2;
    [FieldOffset(0x120)] public SacredFxSurfaceVertexLayout RingVertex3;
    [FieldOffset(0x20A0)] public SacredExpandingBurstFxStateLayout State;
    [FieldOffset(0x252C)] public uint RingTextureHandle;
    [FieldOffset(0x2530)] public uint ParticleTextureHandle;
    [FieldOffset(0x2534)] public uint GroundTextureHandle;
}
