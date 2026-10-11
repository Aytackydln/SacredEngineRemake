using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7A7C30 reads the low byte of event +38 into the element selector.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x39)]
public readonly struct SacredForgeElementFxEventArgumentsLayout
{
    [FieldOffset(0x38)] public readonly byte Element;
}
