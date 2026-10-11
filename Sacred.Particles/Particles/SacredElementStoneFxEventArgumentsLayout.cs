using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered Element Stone input at +38 in the native creation arguments.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x3C)]
public readonly struct SacredElementStoneFxEventArgumentsLayout
{
    [FieldOffset(0x38)] public readonly uint Element;
}
