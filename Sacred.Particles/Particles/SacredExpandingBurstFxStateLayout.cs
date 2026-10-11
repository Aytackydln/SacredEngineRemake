using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Ring of Poison and Kamikaze save 0x48C bytes beginning at object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredExpandingBurstFxStateLayout : IEquatable<SacredExpandingBurstFxStateLayout>
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(8)] public SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public SacredParticleEmissionLayout Emission;

    public readonly bool Equals(SacredExpandingBurstFxStateLayout other)
    {
        var state = this;
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref other, 1)));
    }
    public override readonly bool Equals(object? obj) => obj is SacredExpandingBurstFxStateLayout other && Equals(other);
    public override readonly int GetHashCode()
    {
        var state = this; var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return hash.ToHashCode();
    }
}
