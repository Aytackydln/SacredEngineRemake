using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializer 78BC60 writes this payload from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredFireSkinParticleStateLayout
{
    public const int SerializedSize = 0x4AC;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly float EmissionDuration;
    [FieldOffset(0x008)] public readonly byte Active;
    [FieldOffset(0x009), BinaryUnknown] public readonly ushort Unknown009;
    [FieldOffset(0x00B), BinaryUnknown] public readonly byte Unknown00B;
    [FieldOffset(0x00C)] public readonly SacredParticleVectorLayout EventPosition;
    [FieldOffset(0x018), BinaryUnknown] public readonly ulong Unknown018;
    [FieldOffset(0x020)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x420)] public readonly uint GlowColor;
    [FieldOffset(0x424)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x444)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0x4A8)] public readonly uint Draining;
}
