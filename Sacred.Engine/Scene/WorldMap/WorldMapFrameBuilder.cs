using System;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Rendering;
using Sacred.World.Map;

namespace Sacred.Engine.Scene.WorldMap;

internal static class WorldMapFrameBuilder
{
    private const int MarkerScale = 2;

    public static ScreenFrame Create(WorldMapAtlas atlas, Vector2 playerMapPosition, ulong revision)
    {
        var rgba = (byte[])atlas.Rgba.Clone();
        DrawPlayerMarker(rgba, atlas, playerMapPosition, atlas.PlayerMarker);
        return new ScreenFrame(atlas.Width, atlas.Height, rgba, revision);
    }

    private static void DrawPlayerMarker(
        byte[] destination,
        WorldMapAtlas atlas,
        Vector2 position,
        TextureAsset marker)
    {
        var markerWidth = marker.Width * MarkerScale;
        var markerHeight = marker.Height * MarkerScale;
        var left = (int)MathF.Round(position.X - markerWidth * 0.5f);
        var top = (int)MathF.Round(position.Y - markerHeight * 0.5f);
        for (var y = 0; y < markerHeight; y++)
        for (var x = 0; x < markerWidth; x++)
        {
            var destinationX = left + x;
            var destinationY = top + y;
            if ((uint)destinationX >= atlas.Width || (uint)destinationY >= atlas.Height)
                continue;

            var source = ((y / MarkerScale) * marker.Width + x / MarkerScale) * 4;
            BlendPixel(
                destination,
                (destinationY * atlas.Width + destinationX) * 4,
                marker.Rgba8[source],
                marker.Rgba8[source + 1],
                marker.Rgba8[source + 2],
                marker.Rgba8[source + 3]);
        }
    }

    private static void BlendPixel(
        byte[] destination,
        int offset,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        if (alpha == 0)
            return;

        var inverse = 255 - alpha;
        destination[offset] = (byte)((red * alpha + destination[offset] * inverse) / 255);
        destination[offset + 1] = (byte)((green * alpha + destination[offset + 1] * inverse) / 255);
        destination[offset + 2] = (byte)((blue * alpha + destination[offset + 2] * inverse) / 255);
        destination[offset + 3] = 255;
    }
}
