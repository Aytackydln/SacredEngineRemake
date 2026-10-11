using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7C5180 reads a UInt32 model selector from event +38.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x3C)]
public readonly struct SacredUnderworldGlyphFxEventArgumentsLayout
{
    [FieldOffset(0x38)] public readonly uint Element;
}
