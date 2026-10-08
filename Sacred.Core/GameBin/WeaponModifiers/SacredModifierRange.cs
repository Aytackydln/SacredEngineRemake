using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>Native chance/minimum/maximum triple used for damage, protection and requirements.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SacredModifierRange(int Chance, int Minimum, int Maximum);
