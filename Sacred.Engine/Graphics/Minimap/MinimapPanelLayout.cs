using System;

namespace Sacred.Engine.Graphics.Minimap;

internal readonly record struct MinimapPanelLayout(float X, float Y, float Width, float Height)
{
    public float CenterX => X + Width * 0.5f;
    public float CenterY => Y + Height * 0.5f;

    public static MinimapPanelLayout Calculate(int renderWidth, int renderHeight)
    {
        var width = Math.Max(320.0f, renderWidth * 0.70f);
        var height = Math.Max(240.0f, renderHeight * 0.68f);
        width = Math.Min(width, Math.Max(1.0f, renderWidth - 24.0f));
        height = Math.Min(height, Math.Max(1.0f, renderHeight - 24.0f));
        return new MinimapPanelLayout(
            (renderWidth - width) * 0.5f,
            (renderHeight - height) * 0.5f,
            width,
            height);
    }
}
