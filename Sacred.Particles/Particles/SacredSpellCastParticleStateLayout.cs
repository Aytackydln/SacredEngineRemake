using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold TYPE_FX_CAST_SPELL state at object +20A0; serialized by 0x777220.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSpellCastParticleStateLayout
{
    public const int SerializedSize = 0x49C;
    [FieldOffset(0)] public readonly uint Selector;
    [FieldOffset(4)] public readonly float Elapsed;
    [FieldOffset(8)] public readonly float StartTime;
    [FieldOffset(0xC)] public readonly float EndTime;
    [FieldOffset(0x10)] public readonly float ClockScale;
    [FieldOffset(0x14)] public readonly byte Active;
    [FieldOffset(0x15), BinaryUnknown] public readonly byte Unknown15;
    [FieldOffset(0x16), BinaryUnknown] public readonly ushort Unknown16;
    [FieldOffset(0x18)] public readonly SacredParticleColorTableLayout Colors;
    [FieldOffset(0x418)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x438)] public readonly SacredParticleEmissionLayout Emission;
}
