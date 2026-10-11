using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x253C)]
public struct SacredEmperorFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x5C)] public Vector3 Direction;
    [FieldOffset(0x68)] public uint ParticlesBegin;
    [FieldOffset(0x6C)] public uint ParticlesEnd;
    [FieldOffset(0x74)] public ushort Flags;
    [FieldOffset(0x78)] public uint HitKind;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x98)] public uint RenderRadius;
    [FieldOffset(0x20A0)] public SacredEmperorFxStateLayout State;
    [FieldOffset(0x2534)] public uint ParticleTextureHandle;
    [FieldOffset(0x2538)] public uint UnusedTextureHandle;
}
