using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x48)]
public struct SacredKamikazeFxEventLayout
{
    [FieldOffset(0x44)] public uint RadiusRate;
}
