using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.CombatArts;

/// <summary>Compact art grid in inventory content coordinates, shared by drawing and hit testing.</summary>
public static class CombatArtPanelLayout
{
    public const int Columns = 8;
    public const float IconSize = 56;
    public static HudRectangle Cell(int index) =>
        new(new Vector2(10 + index % Columns * 79, 8 + index / Columns * 82), new(IconSize));

    public static int? HitTest(Vector2 local, int count)
    {
        for (var index = 0; index < count; index++)
            if (Cell(index).Contains(local)) return index;
        return null;
    }
}
