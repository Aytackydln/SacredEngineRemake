using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Three consecutive native float32 coordinates in particle parameter blocks.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredParticleVectorLayout
{
    public const int SerializedSize = 12;
    [FieldOffset(0)] public readonly float X;
    [FieldOffset(4)] public readonly float Y;
    [FieldOffset(8)] public readonly float Z;
    public Vector3 Value => new(X, Y, Z);
}
