using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Core.GameRes;
using Sacred.Inventory.Actors;
using Sacred.UI.CombatArts;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Draws the bound character's rune pool with acquired ranks and native atlas icons.</summary>
internal static class Dx12CombatArtPanel
{
    public static void Build(CombatArtPanelState state, SacredGameActor actor, InventoryPanelAssets assets,
        GameResStore resources, bool gold, ImDrawListPtr draw, IReadOnlyDictionary<string, int> textures,
        Vector2 origin, float scale)
    {
        var artwork = new Dx12UiArtwork(draw, textures, origin, scale);
        var panelSize = new Vector2(InventoryTabsLayout.Width, InventoryTabsLayout.ContentHeight);
        draw.AddRectFilled(origin, origin + panelSize * scale, 0xEA181613);
        var x = 0f;
        draw.PushClipRect(origin, origin + panelSize * scale, true);
        foreach (var background in assets.CombatArts.Backgrounds[actor.CharacterClass])
        {
            artwork.Draw(background, new(x, 0));
            x += background.Size.X;
        }
        draw.PopClipRect();
        draw.AddRect(origin, origin + panelSize * scale, 0xFF837259, 0, ImDrawFlags.None, 2 * scale);
        for (var index = 0; index < state.Available.Count; index++)
        {
            var art = state.Available[index];
            var rank = state.Rank(art.Code);
            var cell = CombatArtPanelLayout.Cell(index);
            var minimum = origin + cell.Position * scale;
            var maximum = minimum + cell.Size * scale;
            DrawIcon(art.Code, gold, draw, textures, minimum, cell.Size * scale,
                rank > 0 ? 0xFFFFFFFF : 0xAA999999);
            if (state.SelectedCode == art.Code)
                draw.AddRect(minimum, maximum, 0xFF32B9DA, 0, ImDrawFlags.None, 2 * scale);
            Text(draw, minimum + new Vector2(0, CombatArtPanelLayout.IconSize + 2) * scale,
                scale * .85f, $"Level {rank}", rank > 0 ? 0xFFE6DFCE : 0xFF908B81);
            var mouse = ImGuiNET.ImGui.GetIO().MousePos;
            if (mouse.X >= minimum.X && mouse.Y >= minimum.Y && mouse.X < maximum.X && mouse.Y < maximum.Y)
            {
                var name = resources.Strings.GetValueOrDefault(art.NameResourceId, $"Combat art {art.Code}");
                ImGuiNET.ImGui.SetTooltip($"{name}\n{(rank > 0 ? $"Level {rank}. Drag to a combat quickslot, or select and click a slot." : "Not learned.")}");
            }
        }
        if (state.Available.Count == 0)
            Text(draw, origin + new Vector2(8, 70) * scale, scale, "No combat arts available.");
    }

    public static void DrawIcon(ushort code, bool gold, ImDrawListPtr draw,
        IReadOnlyDictionary<string, int> textures, Vector2 minimum, Vector2 size, uint tint = 0xFFFFFFFF)
    {
        if (CharacterCombatArtIconSelectors.Resolve(code, gold) is { } icon &&
            textures.TryGetValue(icon.TextureName, out var texture))
            draw.AddImage(texture, minimum, minimum + size,
                new Vector2(icon.X, icon.Y) / 256, new Vector2(icon.X + 64, icon.Y + 64) / 256, tint);
        else
            Text(draw, minimum + new Vector2(4), 1, code.ToString(), tint);
    }

    private static void Text(ImDrawListPtr draw, Vector2 position, float scale, string text, uint color = 0xFFE6DFCE) =>
        draw.AddText(ImGuiNET.ImGui.GetFont(), 14 * scale, position, color, text);
}
