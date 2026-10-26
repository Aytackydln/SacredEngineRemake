using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x559F87..0x55A010. The native caller overwrites its argument
/// with this value before each item-selection retry; preserve that mutation in
/// a full source session. The later rarity draw uses/caps only its low word.</summary>
public static class SacredCreatureLootBonus
{
    public static uint Adjust(uint suppliedBonus, uint sourceReference, ushort experienceMaximum, SacredLootBalance balance)
    {
        var scale = balance.CreatureExperienceBonusScale;
        if (sourceReference <= 16 || scale <= 1.1920929e-7f || experienceMaximum == 0) return suppliedBonus;
        return unchecked((uint)(long)(Math.Sqrt(experienceMaximum * (double)scale) / scale * suppliedBonus));
    }
}
