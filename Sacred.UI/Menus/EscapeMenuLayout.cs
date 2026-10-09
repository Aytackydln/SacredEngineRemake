using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.Menus;

/// <summary>cUI_EscMenu's 1024x768 reference rectangle and text rows.</summary>
public sealed class EscapeMenuLayout(bool hasExport)
{
    public bool HasExport { get; } = hasExport;
    public static Vector2 Size => new(264, 200);
    public const float FontSize = 20;
    public static float GetScale(Vector2 viewport) => BottomHudLayout.GetScale(viewport);
    public static Vector2 ToScreen(Vector2 local, Vector2 viewport) =>
        viewport / 2 + (local - Size / 2) * GetScale(viewport);

    public static HudRectangle GetRowBounds(int index, bool confirmation, Vector2 viewport) =>
        new(ToScreen(new(0, confirmation ? 88 + index * 36 : 8 + index * 36), viewport),
            new Vector2(Size.X, 32) * GetScale(viewport));

    public static int? HitTest(Vector2 point, Vector2 viewport, int count, bool confirmation)
    {
        for (var i = 0; i < count; i++)
            if (GetRowBounds(i, confirmation, viewport).Contains(point)) return i;
        return null;
    }
}
