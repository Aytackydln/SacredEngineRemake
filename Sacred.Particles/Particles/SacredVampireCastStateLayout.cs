using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold vamp_sm_cast state at +20A0; native stream size 1230, including palettes and parameters.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredVampireCastStateLayout
{
    public const int SerializedSize = 0x1230;
    [FieldOffset(0x00)] public readonly float Age;
    [FieldOffset(0x04)] public readonly float Start;
    [FieldOffset(0x08)] public readonly float AnimationDuration;
    [FieldOffset(0x0C)] public readonly float Heading;
    [FieldOffset(0x10)] public readonly float Brightness;
    [FieldOffset(0x14)] public readonly byte Active;
    [FieldOffset(0x18)] public readonly SacredParticleColorTableLayout Palette0;
    [FieldOffset(0x418)] public readonly SacredParticleColorTableLayout Palette1;
    [FieldOffset(0x818)] public readonly SacredParticleColorTableLayout Palette2;
    [FieldOffset(0xC18)] public readonly SacredParticleColorTableLayout Palette3;
    [FieldOffset(0x1018)] public readonly SacredParticleMotionLayout Motion0;
    [FieldOffset(0x1038)] public readonly SacredParticleMotionLayout Motion1;
    [FieldOffset(0x1058)] public readonly SacredParticleMotionLayout Motion2;
    [FieldOffset(0x1078)] public readonly SacredParticleMotionLayout Motion3;
    [FieldOffset(0x1098)] public readonly SacredParticleEmissionLayout Emission0;
    [FieldOffset(0x10FC)] public readonly SacredParticleEmissionLayout Emission1;
    [FieldOffset(0x1160)] public readonly SacredParticleEmissionLayout Emission2;
    [FieldOffset(0x11C4)] public readonly SacredParticleEmissionLayout Emission3;
    [FieldOffset(0x1228)] public readonly ushort CombatArt;
    [FieldOffset(0x122C)] public readonly float Scale;
}
