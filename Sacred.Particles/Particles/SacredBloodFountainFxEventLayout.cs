using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>0x77BE54 reads event +0x3C: 1 green, 2 dark, all other values red.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x40)]
public readonly struct SacredBloodFountainFxEventLayout
{
    [FieldOffset(0x3C)] public readonly uint Palette;
}
