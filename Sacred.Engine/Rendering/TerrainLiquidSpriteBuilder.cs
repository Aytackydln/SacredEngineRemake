using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;

namespace Sacred.Engine.Rendering;

internal sealed class TerrainLiquidSpriteBuilder(AssetManager assets)
{
    private const int RenderTileWidth = 96;
    private const int RenderTileHeight = 48;
    private const int ProjectedOffsetX = 2;
    private const int ProjectedOffsetY = 1;

    private readonly List<TerrainLiquidSprite> _visibleSprites = new(4096);
    private readonly TextureFrameSequenceAsset?[] _animationsByStyle = new TextureFrameSequenceAsset?[256];
    private readonly byte[] _animationStates = new byte[256];
    private bool _assetRequestsPending = true;

    public TerrainLiquidPreparation Prepare(IReadOnlyList<Sector> sectors, bool worldChanged)
    {
        if (!worldChanged && !_assetRequestsPending)
            return new TerrainLiquidPreparation(_visibleSprites, false, 0, 0);

        _visibleSprites.Clear();
        var candidateTiles = 0;
        var missingTiles = 0;
        var requestsPending = false;
        foreach (var sector in sectors)
        {
            candidateTiles += sector.LiquidSurfaces.Count;
            var sectorOriginIso = IsometricProjection.WorldToIso(
                sector.Coord.X * Sector.TileCount,
                sector.Coord.Y * Sector.TileCount);
            foreach (var liquid in sector.LiquidSurfaces.Surfaces)
            {
                var style = LiquidStyle.For(liquid.StyleId);
                if (!TryGetAnimation(liquid.StyleId, style, out var animation, out var requestPending))
                {
                    requestsPending |= requestPending;
                    missingTiles++;
                    continue;
                }

                var localIso = IsometricProjection.WorldToIso(liquid.LocalX, liquid.LocalY);
                var worldX = sector.Coord.X * Sector.TileCount + liquid.LocalX;
                var worldY = sector.Coord.Y * Sector.TileCount + liquid.LocalY;
                var alphas = CornerAlphas(liquid);
                _visibleSprites.Add(new TerrainLiquidSprite(
                    animation!,
                    sector.Coord,
                    sectorOriginIso.X + localIso.X + ProjectedOffsetX,
                    sectorOriginIso.Y + localIso.Y + ProjectedOffsetY,
                    RenderTileWidth,
                    RenderTileHeight,
                    alphas.Left,
                    alphas.Top,
                    alphas.Right,
                    alphas.Bottom,
                    (byte)((worldX & 3) | ((worldY & 3) << 2)),
                    LiquidStyle.AnimationPeriodSeconds));
            }
        }

        _visibleSprites.Sort(static (left, right) =>
        {
            var depth = (left.SectorCoord.X + left.SectorCoord.Y).CompareTo(
                right.SectorCoord.X + right.SectorCoord.Y);
            if (depth != 0)
                return depth;

            var sectorY = left.SectorCoord.Y.CompareTo(right.SectorCoord.Y);
            if (sectorY != 0)
                return sectorY;

            var y = left.IsoY.CompareTo(right.IsoY);
            return y != 0 ? y : left.IsoX.CompareTo(right.IsoX);
        });
        _assetRequestsPending = requestsPending;
        return new TerrainLiquidPreparation(_visibleSprites, true, candidateTiles, missingTiles);
    }

    private bool TryGetAnimation(
        byte styleId,
        LiquidStyle style,
        out TextureFrameSequenceAsset? animation,
        out bool requestPending)
    {
        var state = _animationStates[styleId];
        if (state == 1)
        {
            animation = _animationsByStyle[styleId];
            requestPending = false;
            return true;
        }

        if (state == 2)
        {
            animation = null;
            requestPending = false;
            return false;
        }

        var ready = assets.TryGetTextureFrameSequenceOrRequest(
            style.FrameNameFormat,
            style.FrameCount,
            out animation);
        if (!ready)
        {
            requestPending = true;
            return false;
        }

        requestPending = false;
        if (animation is null)
        {
            _animationStates[styleId] = 2;
            return false;
        }

        _animationsByStyle[styleId] = animation;
        _animationStates[styleId] = 1;
        return true;
    }

    private static LiquidAlphas CornerAlphas(LiquidSurface surface)
    {
        var multiplier = LiquidStyle.For(surface.StyleId).MainAlphaMultiplier;
        return new LiquidAlphas(
            Alpha(surface.AlphaLeft, multiplier),
            Alpha(surface.AlphaTop, multiplier),
            Alpha(surface.AlphaRight, multiplier),
            Alpha(surface.AlphaBottom, multiplier));
    }

    private static byte Alpha(sbyte value, int multiplier) =>
        (byte)Math.Clamp(value * multiplier, 0, 255);

    private readonly record struct LiquidAlphas(byte Left, byte Top, byte Right, byte Bottom);

    private enum LiquidTextureKind
    {
        Water,
        Lava,
        Schwefel
    }

    private readonly record struct LiquidStyle(
        LiquidTextureKind TextureKind,
        string Family,
        int MainAlphaMultiplier,
        int FrameCount)
    {
        public const float AnimationPeriodSeconds = 2.048f;
        public string FrameNameFormat => $"{Family}_{TextureKindName}{{0:00}}.TGA";

        private string TextureKindName => TextureKind switch
        {
            LiquidTextureKind.Lava => "LAVA",
            LiquidTextureKind.Schwefel => "SCHWEFEL",
            _ => "WATER"
        };

        public static LiquidStyle For(byte styleId) => styleId switch
        {
            0 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            1 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            2 => new LiquidStyle(LiquidTextureKind.Water, "C", -12, 50),
            3 => new LiquidStyle(LiquidTextureKind.Water, "D", -12, 50),
            4 => new LiquidStyle(LiquidTextureKind.Lava, "A", -255, 50),
            5 => new LiquidStyle(LiquidTextureKind.Lava, "B", -255, 50),
            6 => new LiquidStyle(LiquidTextureKind.Lava, "C", -255, 20),
            7 => new LiquidStyle(LiquidTextureKind.Schwefel, "A", -255, 20),
            8 => new LiquidStyle(LiquidTextureKind.Lava, "D", -255, 50),
            9 => new LiquidStyle(LiquidTextureKind.Water, "E", -255, 50),
            10 => new LiquidStyle(LiquidTextureKind.Water, "F", -24, 50),
            11 => new LiquidStyle(LiquidTextureKind.Water, "G", -12, 50),
            12 => new LiquidStyle(LiquidTextureKind.Lava, "E", -255, 50),
            13 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            _ => new LiquidStyle(LiquidTextureKind.Water, "C", -12, 50)
        };
    }
}

internal readonly record struct TerrainLiquidPreparation(
    IReadOnlyList<TerrainLiquidSprite> Sprites,
    bool Changed,
    int CandidateTiles,
    int MissingTiles);
