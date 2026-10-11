using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20C0)]
public struct SacredPlongFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    [FieldOffset(0x68)] public uint ParticlesBegin;
    [FieldOffset(0x6C)] public uint ParticlesEnd;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x98)] public uint RenderRadius;
    [FieldOffset(0x20A0)] public SacredPlongFxStateLayout State;
    [FieldOffset(0x20BC)] public uint TextureHandle;
}
