using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Forty native bytes consumed by the two crossed ribbon strips in helper 0x762C30.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredHelicalRibbonParametersLayout
{
    public const int SerializedSize = 0x28;
    [FieldOffset(0x00)] public float Radius;
    [FieldOffset(0x04)] public float HalfWidth;
    [FieldOffset(0x08)] public float HeightStep;
    [FieldOffset(0x0C)] public float AngleStep;
    [FieldOffset(0x10)] public float InitialHeight;
    [FieldOffset(0x14)] public float InitialAngle;
    [FieldOffset(0x18)] public Vector3 Offset;
    [FieldOffset(0x24)] public uint Color;
}
