using System.Numerics;

namespace Sacred.Core.World.Portals;

/// <summary>Native vertex colours and triangle interpolation for the stargate surface.
/// X is grayscale, Y/Z are the old/new minimap alpha. Scene lights do not affect them.</summary>
public static class SacredPortalSurfaceAnimation
{
    public static Vector3 SampleColour(Vector2 uv, float seconds)
    {
        var grid = new Vector2(uv.X * 19, (1 - uv.Y) * 33);
        var cell = Vector2.Min(new(MathF.Floor(grid.X), MathF.Floor(grid.Y)), new(18, 32));
        var fraction = grid - cell;
        var right = VertexColour(cell + new Vector2(1, 0), seconds);
        var above = VertexColour(cell + new Vector2(0, 1), seconds);
        return fraction.X + fraction.Y <= 1
            ? VertexColour(cell, seconds) * (1 - fraction.X - fraction.Y) + right * fraction.X + above * fraction.Y
            : VertexColour(cell + Vector2.One, seconds) * (fraction.X + fraction.Y - 1) +
              right * (1 - fraction.Y) + above * (1 - fraction.X);
    }

    private static Vector3 VertexColour(Vector2 vertex, float seconds)
    {
        var grid = vertex - new Vector2(9.5f, 17);
        var radius = grid.Length();
        var edge = grid - new Vector2(0, Math.Clamp(grid.Y, -7, 7));
        var bucket = Math.Clamp(MathF.Floor((10 - edge.Length()) * 3.2f), 0, 15);
        var phase = seconds % 5;
        var firstAlpha = MathF.Floor(bucket * Math.Clamp(MathF.Floor((5 - phase) * 15.9f), 0, 15) * (17f / 15)) / 255;
        var secondAlpha = MathF.Floor(bucket * Math.Clamp(MathF.Floor((phase - 3) * 15.9f), 0, 15) * (17f / 15)) / 255;
        var slope = radius > .0001f ? grid * (-MathF.Sin(radius - seconds * 5) / radius) : Vector2.Zero;
        var normal = Vector3.Normalize(new(slope.X, -1, slope.Y));
        var shade = MathF.Floor(Math.Clamp((Vector3.Dot(normal, new Vector3(2, -2, 1) / 3) + 1) * 127.9f, 0, 255)) / 255;
        return new(shade, firstAlpha, secondAlpha);
    }
}
