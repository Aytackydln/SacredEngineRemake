using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Core.UI;
using Sacred.UI.Inventory;
using Sacred.UI.Textures;

namespace Sacred.UI.Character;

/// <summary>Unbound cUI_StatisticsChar artwork. Its constructor overrides the requested height to 388.</summary>
public sealed class PlayerStatsLayout
{
    public const int Width = 256;
    public const int Height = 388;
    public IReadOnlyList<PlayerStatsBackground> Backgrounds { get; }
    public IReadOnlyList<EquipmentPanelDecoration> Decorations { get; }

    public PlayerStatsLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        UiTextureRegion Background(string name) => new(table.First(t =>
            SacredUiTextureTable.GetTextureName(t).Equals(name, StringComparison.OrdinalIgnoreCase)));
        Backgrounds = new PlayerStatsBackground[]
        {
            new(Background("GUI_main_ornament.TGA"), new(0, 20), new(256, 286)),
            new(Background("GUI_main_ornament1.TGA"), new(0, 306), new(256, 256))
        };
        var decorations = new List<EquipmentPanelDecoration>();
        for (var i = 0; i < 8; i++)
        {
            var art = new UiTextureRegion(table[i == 0 ? 54 : i == 7 ? 55 : 21]);
            decorations.Add(new(art, new(-4 + i * new UiTextureRegion(table[14]).Size.X, 18 - art.Anchor.Y)));
        }
        var y = 28f;
        for (var i = 0; i < 11; i++)
        {
            var left = new UiTextureRegion(table[i == 10 ? 52 : 34]);
            var right = new UiTextureRegion(table[i == 10 ? 53 : 34]);
            decorations.Add(new(left, new(-4, y)));
            decorations.Add(new(right, new(Width - right.Anchor.X, y)));
            y += right.Size.Y;
        }
        Decorations = decorations;
    }
}

public sealed record PlayerStatsBackground(UiTextureRegion Art, Vector2 Position, Vector2 Size);
