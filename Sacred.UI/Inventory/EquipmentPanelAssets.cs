using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.UI;
using Sacred.UI.Hud;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>Original armor-panel and taskbar hand-slot art from the selected installation.</summary>
public sealed class EquipmentPanelAssets
{
    public IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> Definitions { get; }
    public UiTextureRegion Background { get; }
    public EquipmentPanelFrame Frame { get; }
    public UiTextureRegion WeaponSlotArt { get; }
    public UiTextureRegion WeaponSlotBackground { get; }
    public WeaponQuickSlotLayout QuickSlot { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }

    private EquipmentPanelAssets(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table,
        Dictionary<string, TextureAsset> textures)
    {
        Definitions = table;
        Background = new(table.First(r => SacredUiTextureTable.GetTextureName(r)
            .Equals("GUI_main_ornament.TGA", StringComparison.OrdinalIgnoreCase)));
        Frame = new(table);
        WeaponSlotArt = new(table[103]); // Active taskbar hand frame.
        WeaponSlotBackground = BottomHudSlotBackgrounds.Hand(table);
        QuickSlot = new(table);
        Textures = textures;
    }

    public static async Task<EquipmentPanelAssets> LoadAsync(string gameDirectory,
        Func<string, Task<TextureAsset>> loadTexture)
    {
        var executable = Path.Combine(gameDirectory, "Sacred.exe");
        if (!File.Exists(executable)) executable = Path.Combine(gameDirectory, "pureHD.exe");
        var table = await Task.Run(() => SacredUiTextureTable.Read(executable)).ConfigureAwait(false);
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var assets = new EquipmentPanelAssets(table, textures);
        var names = table.Skip(146).Take(19).Select(SacredUiTextureTable.GetTextureName)
            .Append(assets.Background.TextureName).Concat(assets.Frame.Pieces.Select(p => p.Art.TextureName))
            .Append(assets.WeaponSlotArt.TextureName).Append(assets.WeaponSlotBackground.TextureName);
        if (table.Count > 1611) names = names.Append(SacredUiTextureTable.GetTextureName(table[1611]));
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            textures.Add(name, await loadTexture(name).ConfigureAwait(false));
        return assets;
    }
}
