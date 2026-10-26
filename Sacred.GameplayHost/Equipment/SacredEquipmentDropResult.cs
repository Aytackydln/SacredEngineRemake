using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon.Names;

namespace Sacred.GameplayHost.Equipment;

public sealed record SacredEquipmentDropResult(SacredEquipmentDropRequest Request, SacredItemCategory Category,
    uint? SelectedType, uint FinalRandomState, ReadOnlyMemory<byte> Values, string? UnavailableReason = null, uint? RolledQuality = null)
{
    public byte? RarityTier => Values.Length >= 8 ? (byte)(Values.Span[6] & 15) : null;
    public SacredLootRarityResult? SourceRarity { get; init; }
    public SacredEquipmentNameResult? Name { get; init; }
}
