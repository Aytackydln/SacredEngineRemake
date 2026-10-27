using System;
using System.Numerics;

namespace Sacred.UI.Inventory;

/// <summary>Fits the complete item frame inside the output viewport, independently of world resolution.</summary>
public static class ItemDescriptionPopupLayout
{
    public static (Vector2 Position, float Scale) Place(Vector2 anchor, Vector2 nativeSize, Vector2 viewport, float scale)
    {
        const float margin = 8;
        var available = Vector2.Max(Vector2.One, viewport - new Vector2(margin * 2));
        scale = MathF.Min(scale, MathF.Min(available.X / nativeSize.X, available.Y / nativeSize.Y));
        var size = nativeSize * scale;
        var position = anchor + new Vector2(16 * scale);
        if (position.X + size.X > viewport.X - margin) position.X = anchor.X - size.X - 16 * scale;
        position = Vector2.Clamp(position, new Vector2(margin), Vector2.Max(new Vector2(margin), viewport - size - new Vector2(margin)));
        return (position, scale);
    }

    public static uint ToAbgr(uint argb) => (argb & 0xFF00FF00) | ((argb & 255) << 16) | ((argb >> 16) & 255);
}
