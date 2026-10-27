using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class LootBalanceBinReader
{
    public static SacredLootBalance Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4) throw new InvalidDataException("Truncated Balance.bin header.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (size > file.Length - 4 || size < SacredLootBalanceLayout.RequiredGlobalBytes)
            throw new InvalidDataException("Balance.bin omits loot tuning.");
        var globals = file.Slice(4, checked((int)size));
        var classFactors = new float[SacredLootBalanceLayout.RuneClassLevelFactorCount];
        for (var i = 0; i < classFactors.Length; i++)
            classFactors[i] = BinaryPrimitives.ReadSingleLittleEndian(globals[(SacredLootBalanceLayout.RuneClassLevelFactorsOffset + i * sizeof(float))..]);
        var rewardBonuses = new uint[SacredLootBalanceLayout.CreatureDeathRewardBonusCount];
        for (var i = 0; i < rewardBonuses.Length; i++)
            rewardBonuses[i] = BinaryPrimitives.ReadUInt32LittleEndian(globals[(SacredLootBalanceLayout.CreatureDeathRewardBonusesOffset + i * sizeof(uint))..]);
        return new(CreatureBalanceBinReader.Read(file),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.LevelRatioLowerOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.LevelRatioUpperOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.LevelRatioPlateauOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.RarityWeightFloorOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.SurvivalRarityFactorOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureWeaponChanceOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureArmorChanceOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureMoneyChanceOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreaturePotionChanceOffset..]))
        {
            CreatureBaseChance = BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.CreatureBaseChanceOffset..]),
            CreatureBoostedChanceFactor = BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.CreatureBoostedChanceFactorOffset..]),
            CreaturePartyMemberChanceFactor = BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.CreaturePartyMemberChanceFactorOffset..]),
            CreatureExperienceBonusScale = BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.CreatureExperienceBonusScaleOffset..]),
            CreatureMoneyMinimum = BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureMoneyMinimumOffset..]),
            CreatureMoneyMaximum = BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureMoneyMaximumOffset..]),
            CreatureDeathBaseRollCount = BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureDeathBaseRollCountOffset..]),
            CreatureMarkedExtraRollMaximum = BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureMarkedExtraRollMaximumOffset..]),
            CreatureBoostedExtraRollMaximum = BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureBoostedExtraRollMaximumOffset..]),
            CreatureBoostedRarityBonus = BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureBoostedRarityBonusOffset..]),
            CreatureMarkedRarityBonus = BinaryPrimitives.ReadUInt32LittleEndian(globals[SacredLootBalanceLayout.CreatureMarkedRarityBonusOffset..]),
            DeathEquipmentDetachChance = BinaryPrimitives.ReadInt32LittleEndian(globals[SacredLootBalanceLayout.DeathEquipmentDetachChanceOffset..]),
            DeathRewardBonuses = Array.AsReadOnly(rewardBonuses),
            Runes = new(
                BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.RuneChanceFactorOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.RuneInventoryDivisorOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.RuneBoostedFactorOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(globals[SacredLootBalanceLayout.RuneRatioLimitOffset..]),
                Array.AsReadOnly(classFactors))
        };
    }
}
