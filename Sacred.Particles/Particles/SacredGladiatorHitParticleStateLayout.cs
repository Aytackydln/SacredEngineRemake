using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Gladiator hit serializer 78EFD0 writes this payload from object +20A0; special branch fields remain partial.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredGladiatorHitParticleStateLayout
{
    public const int SerializedSize = 0x4C0;
    [FieldOffset(0x000)] public readonly uint CombatArtType;
    [FieldOffset(0x004)] public readonly float ElapsedTime;
    [FieldOffset(0x008)] public readonly float EmissionDuration;
    [FieldOffset(0x00C)] public readonly float ReleaseAccumulator;
    [FieldOffset(0x010)] public readonly byte Active;
    [FieldOffset(0x011), BinaryUnknown] public readonly byte Unknown011;
    [FieldOffset(0x012), BinaryUnknown] public readonly ushort Unknown012;
    [FieldOffset(0x014)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x414)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x434)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x498), BinaryUnknown] public readonly ulong Unknown498;
    [FieldOffset(0x4a0), BinaryUnknown] public readonly ulong Unknown4A0;
    [FieldOffset(0x4a8), BinaryUnknown] public readonly ulong Unknown4A8;
    [FieldOffset(0x4b0), BinaryUnknown] public readonly ulong Unknown4B0;
    [FieldOffset(0x4b8), BinaryUnknown] public readonly ulong Unknown4B8;
}

