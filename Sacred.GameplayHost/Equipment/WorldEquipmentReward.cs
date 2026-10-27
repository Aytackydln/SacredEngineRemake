using Sacred.Assets.Equipment;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Gold 0x5516B0 orchestration, including both attempts and physical
/// launch rolls. Effects own construction, normalization and publication. An
/// unresolved effect stops the call; rerun only from its initial state.</summary>
public sealed class WorldEquipmentReward(SacredEquipmentRewardRules rules, IReadOnlyList<SacredItemCategory> categories)
{
    public WorldEquipmentReward(SacredEquipmentGenerationData data) : this(
        data.EquipmentRewardRules ?? throw new InvalidOperationException("Original equipment reward rules unavailable."),
        data.CategoryRules?.RewardCategories ?? throw new InvalidOperationException("Original reward categories unavailable.")) { }

    public WorldEquipmentRewardResult Run(ushort sourceLevel, uint initialLevelScratch, SacredEquipmentRewardRequest request,
        SacredRandomSequence random, WorldEquipmentRewardEffects effects)
    {
        for (var call = 0; call < rules.MoneyCalls; call++)
            if (!effects.Money(rules.MoneyResource)) return new(WorldEquipmentRewardPending.Money);
        var hero = effects.HeroResource();
        var levelScratch = initialLevelScratch;
        for (var attempt = 0; attempt < rules.EquipmentAttempts; attempt++)
        {
            var passed = (uint)(random.Next() % 1000) < request.Chance;
            if (passed)
            {
                var query = SacredEquipmentRewardRolls.Query(sourceLevel, hero, request.MaximumQuality, categories, random);
                var resource = effects.Select(query);
                if (!resource.HasValue) return new(WorldEquipmentRewardPending.Selection);
                if (resource != 0)
                {
                    var created = effects.Create(resource.Value);
                    if (!created.HasValue) return new(WorldEquipmentRewardPending.Creation);
                    var item = created.Value;
                    if (item.Physical)
                    {
                        if (item.Item && item.ValuesResource.HasValue)
                        {
                            // Native replaces only the low word of this scratch
                            // DWORD. Its first high word is caller stack context;
                            // later launches overwrite it with the last spin float.
                            var nativeLevel = unchecked((int)((levelScratch & 0xFFFF0000) | sourceLevel));
                            var generation = SacredEquipmentRewardRolls.Generation(nativeLevel, request, random);
                            if (!effects.Normalize(item, generation)) return new(WorldEquipmentRewardPending.Normalization);
                        }
                        // Native 16F publication and physical reset/height precede
                        // the five launch draws; 140 publication follows launch.
                        if (!effects.PrepareLaunch(item)) return new(WorldEquipmentRewardPending.PrepareLaunch);
                        var launch = SacredEquipmentRewardRolls.Launch(random);
                        levelScratch = BitConverter.SingleToUInt32Bits(launch.SpinZ);
                        if (!effects.Launch(item, launch)) return new(WorldEquipmentRewardPending.Launch);
                    }
                }
            }
            request = SacredEquipmentRewardRolls.NextAttempt(request, passed);
        }
        return new(WorldEquipmentRewardPending.None);
    }
}
