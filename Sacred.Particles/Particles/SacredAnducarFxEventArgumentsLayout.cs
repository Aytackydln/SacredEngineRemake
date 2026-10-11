using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7C05C4..7C05EC uses these endpoint differences only for the per-model delay.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x38)]
public readonly struct SacredAnducarFxEventArgumentsLayout
{
    [FieldOffset(0x20)] public readonly Vector3 FirstPoint;
    [FieldOffset(0x2C)] public readonly Vector3 SecondPoint;
}
