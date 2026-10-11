using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x2534)]
public struct SacredBloodFountainFxObjectLayout
{
    [FieldOffset(0x08)] public Vector3 Position;
    // The 0x653EA0 helper multiplies the jet vector by these three matrix rows.
    [FieldOffset(0x14)] public Vector3 OrientationX;
    [FieldOffset(0x20)] public Vector3 OrientationY;
    [FieldOffset(0x2C)] public Vector3 OrientationZ;
    [FieldOffset(0x68)] public uint ParticlesBegin;
    [FieldOffset(0x6C)] public uint ParticlesEnd;
    [FieldOffset(0x7C)] public uint Notification;
    [FieldOffset(0x98)] public uint UserData;
    [FieldOffset(0x9C)] public int LightRadius;
    [FieldOffset(0x20A0)] public SacredBloodFountainFxStateLayout State;
    [FieldOffset(0x2530)] public uint ParticleTextureHandle;
}
