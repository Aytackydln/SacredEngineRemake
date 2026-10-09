using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Dragon Ground Fire payload at +0x20A0; serializer 0x7AB350.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDragonGroundFireParticleStateLayout
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    /// <summary>Event +0x38 byte; native render selects one of three constructor texture handles.</summary>
    [FieldOffset(0x005)] public readonly byte TextureSelector;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
}
