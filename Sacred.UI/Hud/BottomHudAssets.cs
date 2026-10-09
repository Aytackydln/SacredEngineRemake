using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;

namespace Sacred.UI.Hud;

/// <summary>Loads native UI definitions and resolves their artwork through the selected game's archives.</summary>
public sealed class BottomHudAssets
{
    // cUI_Taskbar2 loads these two textures by name, independently of sUITextureDef.
    public const string DayNightDisc = "GUI_daynightdisc.TGA";
    public const string DayNightMask = "GUI_daynightalpha.TGA";
    public BottomHudLayout Layout { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }

    private BottomHudAssets(BottomHudLayout layout, Dictionary<string, TextureAsset> textures)
    {
        Layout = layout;
        Textures = textures;
    }

    public static async Task<BottomHudAssets> LoadAsync(string gameDirectory,
        Func<string, Task<TextureAsset>> loadTexture)
    {
        var executable = Path.Combine(gameDirectory, "Sacred.exe");
        if (!File.Exists(executable)) executable = Path.Combine(gameDirectory, "pureHD.exe");
        var table = await Task.Run(() => SacredUiTextureTable.Read(executable)).ConfigureAwait(false);
        var layout = new BottomHudLayout(table);
        var names = layout.Buttons.SelectMany(b => new[] { b.Up.TextureName, b.Down.TextureName })
            .Concat(layout.Decorations.Select(d => d.Region.TextureName))
            .Concat(layout.QuickSlots.Wings.Select(d => d.Region.TextureName))
            .Concat(layout.QuickSlots.CombatArtSlots.Select(d => d.Region.TextureName))
            .Append(layout.QuickSlots.HandSlot.Region.TextureName)
            .Append(layout.QuickSlots.HandBackground.Region.TextureName)
            .Concat(layout.QuickSlots.CombatArtBackgrounds.Select(d => d.Region.TextureName))
            .Concat(new[] { layout.Panel.TextureName, DayNightDisc, DayNightMask })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var textures = await Task.WhenAll(names.Select(loadTexture)).ConfigureAwait(false);
        return new(layout, names.Select((name, index) => (name, textures[index]))
            .ToDictionary(pair => pair.name, pair => pair.Item2, StringComparer.OrdinalIgnoreCase));
    }
}
