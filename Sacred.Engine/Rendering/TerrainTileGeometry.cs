using System;
using System.Collections.Generic;
using Sacred.Core.World.Elevation;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.Engine.Rendering;

internal static class TerrainTileGeometry
{
    public const int Width = 96;
    public const int Height = 48;

    private const float HalfWidth = Width * 0.5f;
    private const float HalfHeight = Height * 0.5f;

    public static TerrainCompositionBounds CalculateSectorBounds(Sector sector)
    {
        var bounds = new BoundsAccumulator();
        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            var iso = IsometricProjection.WorldToIso(localX, localY);
            var surface = GetSurface(sector, localX, localY);
            bounds.IncludeTile(
                iso.X,
                iso.Y,
                surface.VisualElevation);
        }

        return bounds.ToBounds();
    }

    public static TerrainCompositionBounds CropTiles(List<TerrainCompositionTile> tiles)
    {
        if (tiles.Count == 0)
            return new TerrainCompositionBounds(0, 0, 1, 1);

        var bounds = new BoundsAccumulator();
        foreach (var tile in tiles)
        {
            bounds.IncludeTile(
                tile.ScreenX,
                tile.ScreenY,
                tile.Surface.VisualElevation);
        }
        var result = bounds.ToBounds();

        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            tiles[index] = tile with
            {
                ScreenX = tile.ScreenX - result.X,
                ScreenY = tile.ScreenY - result.Y
            };
        }

        return result;
    }

    public static TerrainTileSurface GetSurface(Sector sector, int localX, int localY)
        => new(sector.VisualElevation[localX, localY], sector.BakedLight[localX, localY]);

    private sealed class BoundsAccumulator
    {
        private float _minimumX = float.PositiveInfinity;
        private float _minimumY = float.PositiveInfinity;
        private float _maximumX = float.NegativeInfinity;
        private float _maximumY = float.NegativeInfinity;

        public void IncludeTile(
            float x,
            float y,
            TerrainVisualElevationTile elevation)
        {
            IncludeVertex(x, y + HalfHeight, elevation.SouthWest);
            IncludeVertex(x + HalfWidth, y, elevation.NorthWest);
            IncludeVertex(x + Width, y + HalfHeight, elevation.NorthEast);
            IncludeVertex(x + HalfWidth, y + Height, elevation.SouthEast);
        }

        public TerrainCompositionBounds ToBounds()
        {
            var x = (int)MathF.Floor(_minimumX);
            var y = (int)MathF.Floor(_minimumY);
            var right = (int)MathF.Ceiling(_maximumX);
            var bottom = (int)MathF.Ceiling(_maximumY);
            return new TerrainCompositionBounds(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
        }

        private void IncludeVertex(float x, float y, sbyte visualElevation)
        {
            y -= visualElevation;
            _minimumX = MathF.Min(_minimumX, x);
            _minimumY = MathF.Min(_minimumY, y);
            _maximumX = MathF.Max(_maximumX, x);
            _maximumY = MathF.Max(_maximumY, y);
        }
    }
}

internal readonly record struct TerrainCompositionBounds(int X, int Y, int Width, int Height);
