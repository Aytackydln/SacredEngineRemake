using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Dwarf Shoot Impact payload at +0x20A0; serializer 0x7B9530.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDwarfShootImpactParticleStateLayout
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005)] public readonly byte BurstEmitted;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
}
