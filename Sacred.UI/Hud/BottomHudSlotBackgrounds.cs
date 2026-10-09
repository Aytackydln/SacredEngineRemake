using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Assets.Executable;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Hud;

/// <summary>Native selected-empty slot artwork, resolved across Demo and Gold table ordering.</summary>
internal static class BottomHudSlotBackgrounds
{
    public static UiTextureRegion Hand(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table) =>
        Read(table, "GUI_spell03.TGA");

    public static UiTextureRegion CombatArt(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table) =>
        Read(table, "GUI_spell01.TGA");

    private static UiTextureRegion Read(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table,
        string textureName) => new(table.Single(record =>
        // The native backgrounds occupy the bottom-right cell of their respective sheets.
        // Keep the matched record: size, UV conversion and texture loading use its original bytes.
        record.U0 == 192 && record.V0 == 192 && record.U1 == 255 && record.V1 == 255 &&
        string.Equals(SacredUiTextureTable.GetTextureName(record), textureName, StringComparison.OrdinalIgnoreCase)));
}
