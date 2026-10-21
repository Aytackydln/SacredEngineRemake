using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 790B30 writes this 0x4D0 payload from object +20A0; initialized by 790CE0.
/// Texture handles at +2570/+2574 belong to the renderer and follow this payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSeraphimCastParticleStateLayout
{
    public const int SerializedSize = 0x4D0;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float SimulationStart;
    [FieldOffset(0x008)] public readonly float EmissionEnd;
    [FieldOffset(0x00C)] public readonly byte Active;
    [FieldOffset(0x00D), BinaryUnknown] public readonly ushort Unknown00D;
    [FieldOffset(0x00F), BinaryUnknown] public readonly byte Unknown00F;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x410)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x430)] public readonly SacredParticleEmissionLayout Emission;
    // +494..+4C7 is used by the other Seraphim cast branch and remains unmapped.
    [FieldOffset(0x4C8)] public readonly ushort CombatArtType;
    [FieldOffset(0x4CA), BinaryUnknown] public readonly ushort Unknown4CA;
    [FieldOffset(0x4CC)] public readonly float StrengthFactor;
}
