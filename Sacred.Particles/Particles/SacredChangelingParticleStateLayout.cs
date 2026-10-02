using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Changeling payload at object +0x20A0, serialized by 0x795C70.
/// The texture handle at +0x2190 lies outside the 0xF0-byte payload.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredChangelingParticleStateLayout
{
    public const int SerializedSize = 0xF0;
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04)] public readonly byte Active;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x08)] public readonly uint Color0;
    [FieldOffset(0x0C)] public readonly uint Color1;
    [FieldOffset(0x10)] public readonly uint Color2;
    [FieldOffset(0x14)] public readonly uint Color3;
    [FieldOffset(0x18)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x38)] public readonly SacredParticleEmissionLayout Emission;
    // Gold writes fourteen creature types at 0x795F3D..0x795FC1 and chooses
    // one via the remainder of division by fourteen at 0x7960BE.
    [FieldOffset(0x9C)] public readonly SacredChangelingAnimalTypeListLayout AnimalTypes;
    [FieldOffset(0xD4), BinaryUnknown] public readonly SacredChangelingTrailingStateLayout Unknown0D4;
}

[InlineArray(14)]
public struct SacredChangelingAnimalTypeListLayout
{
    private uint _element0;
}

[InlineArray(0x1C)]
public struct SacredChangelingTrailingStateLayout
{
    private byte _element0;
}
