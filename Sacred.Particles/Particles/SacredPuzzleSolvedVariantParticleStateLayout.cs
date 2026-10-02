using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold cParticleSystem_puzzleSolved_v state at +0x20A0, serialized by
/// 0x76D0B0. Event +0x38 selects the subtype; subtypes 2 and 3 write fixed radial bursts.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredPuzzleSolvedVariantParticleStateLayout
{
    public const int SerializedSize = 0x490;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x408)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x428)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x48C)] public readonly byte EventSubtype;
    [FieldOffset(0x48D), BinaryUnknown] public readonly byte Unknown48D;
    [FieldOffset(0x48E), BinaryUnknown] public readonly ushort Unknown48E;
}
