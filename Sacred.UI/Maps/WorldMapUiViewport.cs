using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.Maps;

/// <summary>Reflows the original map ornaments around the output without stretching their artwork.</summary>
public readonly struct WorldMapUiViewport(Vector2 size)
{
    public Vector2 Size { get; } = size;
    public float Scale => BottomHudLayout.GetScale(Size);

    // The native corner inset stays fixed; only the tiled edge lengths grow.
    public HudRectangle FrameInset => new(new(48, 48), Size / Scale - new Vector2(96));

    // Place the map just inside the thin ornament, rather than the original 70px mask.
    public HudRectangle MapBounds => new(new Vector2(24) * Scale, Size - new Vector2(48) * Scale);

    public Vector2 FrameToScreen(Vector2 position) => position * Scale;

    public bool ContainsFrame(Vector2 point)
    {
        var cornerInset = new Vector2(48) * Scale;
        return !MapBounds.Contains(point) ||
            ((point.X < cornerInset.X || point.X >= Size.X - cornerInset.X) &&
             (point.Y < cornerInset.Y || point.Y >= Size.Y - cornerInset.Y));
    }

    public Vector2 ControlsToScreen(Vector2 authored) =>
        new Vector2(Size.X / 2, Size.Y) + (authored - new Vector2(512, 768)) * Scale;
}
