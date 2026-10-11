using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x24)]
public struct SacredSuperfluidFxEventArgumentsLayout
{
    [FieldOffset(0x20)] public float ElapsedRate;
}
