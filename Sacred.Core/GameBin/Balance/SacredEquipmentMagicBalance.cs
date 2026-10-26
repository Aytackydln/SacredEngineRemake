namespace Sacred.Core.GameBin.Balance;

public sealed record SacredEquipmentMagicBalance(
    IReadOnlyDictionary<uint, IReadOnlyList<SacredEquipmentMagicPoolLayout>> Pools,
    IReadOnlyDictionary<uint, uint> RarityWeights)
{
    public IReadOnlyList<SacredEquipmentMagicPoolLayout> Pool(uint weaponType) => Pools.GetValueOrDefault(weaponType) ?? [];
}
