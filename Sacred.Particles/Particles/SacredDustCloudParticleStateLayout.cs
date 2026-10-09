using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold cParticleSystem_dustcloud state at +0x20A0. Serializer 0x7C2CA0
/// transfers 0x48C bytes; the texture handle at object +0x252C is outside the payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDustCloudParticleStateLayout
{
    public const int SerializedSize = 0x48C;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    /// <summary>Set after the first advanceTime calls stdCreation; later updates only move particles.</summary>
    [FieldOffset(0x005)] public readonly byte BurstEmitted;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
}
