using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>0xD9C-byte state block written from cParticleSystem_smoke +0x20A0
/// by Sacred.exe 0x76E7D0. The serializer precedes it with its UInt32 byte length
/// and follows it with 0xDEADC0DE and base-system state. Its enclosing save-file
/// location has not been recovered. Gaps remain unmapped, not inferred padding.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSmokeParticleStateLayout
{
    public const int SerializedSize = 0xD9C;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    /// <summary>Local preset selector supplied by the create-object dispatch. Not an item ID.</summary>
    [FieldOffset(0x008)] public readonly int Preset;
    [FieldOffset(0x00C)] public readonly SacredParticleColorTableLayout Colors0;
    [FieldOffset(0x40C)] public readonly SacredParticleColorTableLayout Colors1;
    [FieldOffset(0x80C)] public readonly SacredParticleColorTableLayout Colors2;
    [FieldOffset(0xC0C)] public readonly SacredParticleMotionLayout Motion0;
    [FieldOffset(0xC2C)] public readonly SacredParticleMotionLayout Motion1;
    [FieldOffset(0xC4C)] public readonly SacredParticleMotionLayout Motion2;
    [FieldOffset(0xC6C)] public readonly SacredParticleEmissionLayout Emission0;
    [FieldOffset(0xCD0)] public readonly SacredParticleEmissionLayout Emission1;
    [FieldOffset(0xD34)] public readonly SacredParticleEmissionLayout Emission2;
    /// <summary>Geyser countdown reduced by dt; negative values reset the random
    /// quiet period, values below one enter the high-speed burst (0x76E3FF).</summary>
    [FieldOffset(0xD98)] public readonly float BurstCountdown;
    public float UnknownD98 => BurstCountdown;
}
