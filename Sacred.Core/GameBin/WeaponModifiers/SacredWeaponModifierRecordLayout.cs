using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>The fixed portion of a Gold wpmod.bin record, followed by its bonus entries.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredWeaponModifierRecordLayout
{
    public const int Size = SacredWeaponModifierLayout.Size + sizeof(uint);

    [FieldOffset(0)]
    public readonly SacredWeaponModifierLayout Definition;

    /// <summary>Number of serialized <see cref="SacredWeaponModifierBonusLayout"/> entries following this record.</summary>
    [FieldOffset(SacredWeaponModifierLayout.Size)]
    public readonly uint BonusCount;
}
