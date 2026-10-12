using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.Character;

/// <summary>cUI_Mercenary's player plus four companion entries, anchored to the top-right edge.</summary>
public sealed class ActorPortraitLayout(ActorPortraitAssets assets)
{
    public const int MaximumCompanions = 4;
    public const int ColumnWidth = 92;
    public Vector2 RingSize => assets.HealthFull.Size;
    public Vector2 Position(int index) => new(0, 16 + index * (assets.HealthEmpty.Size.Y + 24));
    public static Vector2 PortraitOffset => new(9, 18);
    public static Vector2 PortraitSize => new(64, 74);
    public static Vector2 ExperienceOffset => new(8, 106);
    public static Vector2 Origin(Vector2 viewport) => new(viewport.X - ColumnWidth * BottomHudLayout.GetScale(viewport), 0);
    public HudRectangle Bounds(int index, Vector2 viewport) =>
        new(Origin(viewport) + Position(index) * BottomHudLayout.GetScale(viewport),
            new Vector2(RingSize.X, RingSize.Y + 16) * BottomHudLayout.GetScale(viewport));
}
