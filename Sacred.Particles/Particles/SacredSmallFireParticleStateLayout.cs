using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold +20A0, serialized as 0x918 bytes at 0x7A0020. Four texture handles follow.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSmallFireParticleStateLayout
{
    public const int SerializedSize = 0x918;
    [FieldOffset(0x000)] public readonly float ElapsedTime;
    [FieldOffset(0x004)] public readonly byte Active;
    [FieldOffset(0x005), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x008)] public readonly float FlareElapsedTime;
    [FieldOffset(0x00C)] public readonly byte Subtype;
    [FieldOffset(0x00D), BinaryUnknown] public readonly byte Unknown00D;
    [FieldOffset(0x00E), BinaryUnknown] public readonly ushort Unknown00E;
    [FieldOffset(0x010)] public readonly SacredParticleColorTableLayout FireColors;
    [FieldOffset(0x410)] public readonly SacredParticleColorTableLayout SmokeColors;
    [FieldOffset(0x810)] public readonly SacredParticleMotionLayout FireMotion;
    [FieldOffset(0x830)] public readonly SacredParticleMotionLayout SmokeMotion;
    [FieldOffset(0x850)] public readonly SacredParticleEmissionLayout FireEmission;
    [FieldOffset(0x8B4)] public readonly SacredParticleEmissionLayout SmokeEmission;
}
