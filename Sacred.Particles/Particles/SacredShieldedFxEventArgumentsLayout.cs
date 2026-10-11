using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredShieldedFxEventArgumentsLayout
{
    public const int SerializedSize = 0x24;
    [FieldOffset(0x20)] public float ElapsedRate;
}
