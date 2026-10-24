using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.UI;
using Sacred.Inventory.Actors;
using Sacred.UI.Textures;

namespace Sacred.UI.CombatArts;

/// <summary>Loads the combat-art atlases named by the installed executable's UI table.</summary>
public sealed class CombatArtPanelAssets
{
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }
    public IReadOnlyDictionary<SacredCharacterClass, IReadOnlyList<UiTextureRegion>> Backgrounds { get; }

    private CombatArtPanelAssets(Dictionary<string, TextureAsset> textures,
        IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions)
    {
        Textures = textures;
        Backgrounds = Enum.GetValues<SacredCharacterClass>().ToDictionary(character => character,
            character => (IReadOnlyList<UiTextureRegion>)definitions
                .Where(record => SacredUiTextureTable.GetTextureName(record)
                    .StartsWith($"GUI_HERO_{Suffix(character)}", StringComparison.OrdinalIgnoreCase))
                .Select(record => new UiTextureRegion(record)).OrderBy(art => art.TextureName).ToArray());
    }

    private static string Suffix(SacredCharacterClass character) => character switch
    {
        SacredCharacterClass.Seraphim => "SERA",
        SacredCharacterClass.Gladiator => "GLAD",
        SacredCharacterClass.BattleMage => "MAGE",
        SacredCharacterClass.DarkElf => "DELF",
        SacredCharacterClass.WoodElf => "WELF",
        SacredCharacterClass.Vampiress => "VAMP",
        SacredCharacterClass.Dwarf => "DWA",
        SacredCharacterClass.Daemon => "DAE",
        _ => throw new ArgumentOutOfRangeException(nameof(character))
    };

    public static async Task<CombatArtPanelAssets> LoadAsync(
        IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions,
        Func<string, Task<TextureAsset>> loadTexture)
    {
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var names = definitions.Select(SacredUiTextureTable.GetTextureName)
            .Where(name => name.StartsWith("GUI_spell", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("GUI_HERO_", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) textures.Add(name, await loadTexture(name).ConfigureAwait(false));
        return new(textures, definitions);
    }
}
