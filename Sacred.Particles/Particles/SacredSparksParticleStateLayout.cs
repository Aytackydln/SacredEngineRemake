using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold cParticleSystem_sparks state at +0x20A0. Serializer 0x7AF130
/// transfers 0x490 bytes; the texture handle at object +0x2530 is outside the payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSparksParticleStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    /// <summary>Event +0x38; only subtype zero has a recovered parameter recipe.</summary>
    [FieldOffset(0x008)] public readonly uint EventSubtype;
    [FieldOffset(0x00C)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x40C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x42C)] public readonly SacredParticleEmissionLayout Emission;
}
