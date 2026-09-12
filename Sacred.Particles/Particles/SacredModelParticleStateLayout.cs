using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>0xA0 serialized bytes starting at native object +0x20A0 in TORCHSMOKE
/// (serializer 0x775100) and MAGICWORMS (0x77C6C0). Texture handles are outside this block.</summary>
/// <remarks>demo confirms sSystemInfo: Time, alive, state, colors[4],
/// psMove (PS_STD_MOVEMENT), psCreate (PS_STD_CREATION).</remarks>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredModelParticleStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0)] public readonly float Elapsed;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(8)] public readonly uint EmissionStage;
    [FieldOffset(0xC)] public readonly uint CornerColor0;
    [FieldOffset(0x10)] public readonly uint CornerColor1;
    [FieldOffset(0x14)] public readonly uint CornerColor2;
    [FieldOffset(0x18)] public readonly uint CornerColor3;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x3C)] public readonly SacredParticleEmissionLayout Emission;
}

/// <summary>0xA4 serialized sSystemInfo used by MAGICFIRE and MAGICGIFT.
/// Native analysis identifies the trailing float as intensity.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredElementalWeaponParticleStateLayout
{
    public const int SerializedSize = 0xA4;
    [FieldOffset(0)] public readonly float Elapsed;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(8)] public readonly uint EmissionStage;
    [FieldOffset(0xC)] public readonly uint CornerColor0;
    [FieldOffset(0x10)] public readonly uint CornerColor1;
    [FieldOffset(0x14)] public readonly uint CornerColor2;
    [FieldOffset(0x18)] public readonly uint CornerColor3;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x3C)] public readonly SacredParticleEmissionLayout Emission;
    [FieldOffset(0xA0)] public readonly float Intensity;
}

/// <summary>One constrained chain node: position followed by retained displacement.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x18)]
public readonly struct SacredModelTrailPointLayout
{
    public readonly SacredParticleVectorLayout Position;
    public readonly SacredParticleVectorLayout Displacement;
}

[InlineArray(50)]
public struct SacredModelTrailPoints
{
    private SacredModelTrailPointLayout _first;
}

/// <summary>MAGICWHIP/MAGICSTREAK serialized state at native object +0x20A0;
/// serializers 0x7875B0/0x787F70. Draw quads and texture handles follow this block.</summary>
/// <remarks>Demo names: time, alive, ropePnts[50], ropeMass. Each sRopePnt
/// contains pos and imp. Demo and Gold share these serialized offsets.</remarks>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredModelTrailStateLayout
{
    public const int SerializedSize = 0x4BC;
    [FieldOffset(0)] public readonly float Elapsed;
    [FieldOffset(4)] public readonly byte Active;
    [FieldOffset(8)] public readonly SacredModelTrailPoints Points;
    /// <summary>13.5 in both constructors. Update multiplies by 0.05 to retain 0.675 of displacement.</summary>
    [FieldOffset(0x4B8)] public readonly float Inertia;
}
