using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.UI.Character;
using Sacred.UI.CombatArts;

namespace Sacred.UI.Inventory;

/// <summary>Composes character-column artwork and the tabbed inventory's combat-art atlases.</summary>
public sealed class InventoryPanelAssets
{
    public EquipmentPanelAssets Equipment { get; }
    public PlayerStatsAssets Stats { get; }
    public CombatArtPanelAssets CombatArts { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }

    private InventoryPanelAssets(EquipmentPanelAssets equipment, PlayerStatsAssets stats, CombatArtPanelAssets combatArts)
    {
        Equipment = equipment; Stats = stats;
        CombatArts = combatArts;
        Textures = equipment.Textures.Concat(stats.Textures).Concat(combatArts.Textures)
            .DistinctBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<InventoryPanelAssets> LoadAsync(string gameDirectory,
        Func<string, Task<TextureAsset>> loadTexture)
    {
        var equipment = await EquipmentPanelAssets.LoadAsync(gameDirectory, loadTexture).ConfigureAwait(false);
        var stats = await PlayerStatsAssets.LoadAsync(equipment.Definitions, loadTexture).ConfigureAwait(false);
        var combatArts = await CombatArtPanelAssets.LoadAsync(equipment.Definitions, loadTexture).ConfigureAwait(false);
        return new(equipment, stats, combatArts);
    }
}
