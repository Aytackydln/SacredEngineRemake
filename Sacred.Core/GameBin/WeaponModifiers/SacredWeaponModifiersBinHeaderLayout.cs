using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>The file-level wpmod.bin header preceding variable-length modifier records.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredWeaponModifiersBinHeaderLayout
{
    public const int Size = sizeof(uint);

    /// <summary>Gold also reads this count at 0x494161 to select a count * 224 byte fingerprint prefix.
    /// That compatibility prefix length is not the size of the serialized records.</summary>
    [FieldOffset(0)] public readonly uint RecordCount;
}
