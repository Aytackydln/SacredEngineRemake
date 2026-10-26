namespace Sacred.Core.Loot;

/// <summary>Gold 0x564A60: effective find-special-items stat. The equipment sum
/// is supplied by 0x570ED0, including enabled socket records. Four native hero
/// progression unlocks multiply the rounded word in sequence.</summary>
public static class SacredLootFind
{
    /// <param name="unlockMask">Bits 0..3 correspond to progression-vector
    /// entries 3 >= 8, 9 >= 7, 21 >= 6 and 53 >= 7. Their gameplay names remain unresolved.</param>
    public static ushort Calculate(float equipmentBonus, uint difficulty, byte unlockMask = 0)
    {
        if (!float.IsFinite(equipmentBonus)) throw new ArgumentOutOfRangeException(nameof(equipmentBonus));
        var result = unchecked((ushort)(long)(equipmentBonus + 100d));
        var factor = difficulty switch { 1 => 1.34f, 2 => 1.67f, 3 => 2f, 4 => 2.34f, _ => 1f };
        for (var bit = 0; bit < 4; bit++)
            if ((unlockMask & (1 << bit)) != 0)
                result = unchecked((ushort)(long)((factor * (double).2f + 1d) * result + .5d));
        return result;
    }
}
