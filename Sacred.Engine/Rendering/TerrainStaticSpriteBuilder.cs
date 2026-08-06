using System;
using System.Collections.Generic;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Rendering;

internal sealed class TerrainStaticSpriteBuilder(AssetManager assets)
{
    private const uint NormalRenderExcludeFlags = 0x290;
    private const uint NightOnlyObjectFlag = 0x00000040;
    private const uint UnlitGraphicFlag = 0x00020000;
    private const int ExteriorActiveLayer = 1;
    private const byte SpecialRenderClass = 0x0C;
    private const uint RearGraphicFlag = 0x00000004;
    private const uint FrontGraphicFlag = 0x00800000;
    private const float ObjectShiftX = 47.8f;
    private const float ObjectShiftY = -0.3f;
    private const float LightHaloDiameterScale = 1.2f;

    private readonly List<TerrainStaticSprite> _visibleSprites = new(1024);
    private readonly List<TerrainWorldLight> _visibleLights = new(64);
    private readonly WorldLightAppearanceCache _lightAppearanceCache = new();
    private bool _assetRequestsPending = true;
    private bool _nightObjectsVisible;

    public bool HasPendingAssetRequests => _assetRequestsPending;

    public TerrainStaticPreparation Prepare(
        IReadOnlyList<Sector> sectors,
        bool worldChanged,
        bool nightObjectsVisible)
    {
        var nightVisibilityChanged = _nightObjectsVisible != nightObjectsVisible;
        _nightObjectsVisible = nightObjectsVisible;
        if (!worldChanged && !nightVisibilityChanged && !_assetRequestsPending)
            return new TerrainStaticPreparation(_visibleSprites, _visibleLights, false, 0, 0);

        _visibleSprites.Clear();
        _visibleLights.Clear();
        var candidateObjects = 0;
        var missingObjects = 0;
        var requestsPending = false;
        foreach (var sector in sectors)
        {
            candidateObjects += sector.StaticObjects.Count;
            foreach (var staticObject in sector.StaticObjects.Objects)
            {
                if ((staticObject.Flags & NormalRenderExcludeFlags) != 0 ||
                    staticObject.SurfaceRenderLayer > ExteriorActiveLayer)
                {
                    continue;
                }

                if (!assets.TryGetStaticSpriteOrRequest(staticObject.TypeId, out var sprite))
                {
                    requestsPending = true;
                    missingObjects++;
                    continue;
                }

                if (sprite is null)
                {
                    var ready = assets.TryGetMiniObjectSpriteOrRequest(
                        staticObject.TypeId,
                        staticObject.SpriteParam2E,
                        staticObject.SpriteParam2F,
                        staticObject.OrientationOrFrame,
                        staticObject.AnimationFrameDurationTicks,
                        staticObject.AnimationFrameCount,
                        out sprite);
                    if (!ready)
                    {
                        requestsPending = true;
                        missingObjects++;
                        continue;
                    }
                }

                if (sprite is null)
                {
                    missingObjects++;
                    continue;
                }

                var footX = staticObject.ProjectedX + ObjectShiftX;
                var footY = staticObject.ProjectedY + ObjectShiftY;
                var spriteIsoX = footX - sprite.AnchorX;
                var spriteIsoY = footY - sprite.AnchorY;
                if (Math.Abs(spriteIsoX) > 1048576 || Math.Abs(spriteIsoY) > 1048576)
                {
                    missingObjects++;
                    continue;
                }

                var item = assets.GetItem(staticObject.TypeId);
                if (item is { } lightItem &&
                    _lightAppearanceCache.TryGet(lightItem, sprite, out var lightAppearance))
                {
                    var diameter = lightItem.ModelDesc.ModelExtent * LightHaloDiameterScale;
                    _visibleLights.Add(new TerrainWorldLight(
                        sprite,
                        spriteIsoX + lightAppearance.CenterX - diameter * 0.5f,
                        spriteIsoY + lightAppearance.CenterY - diameter * 0.5f,
                        diameter,
                        lightAppearance.Colour,
                        lightAppearance.Opacity));
                }

                if ((staticObject.Flags & NightOnlyObjectFlag) != 0 &&
                    !nightObjectsVisible &&
                    sprite.FrameCount <= 1)
                    continue;

                _visibleSprites.Add(new TerrainStaticSprite(
                    sprite,
                    IsUnlit(item),
                    spriteIsoX,
                    spriteIsoY,
                    footX,
                    footY,
                    staticObject.SurfaceRenderLayer,
                    EngineQueueIndex(staticObject),
                    staticObject.TileDepth,
                    staticObject.TileWorldY,
                    staticObject.TileWorldX,
                    staticObject.ChainDepth,
                    staticObject.InsertionOrder));
            }
        }

        _visibleSprites.Sort(CompareSprites);
        _assetRequestsPending = requestsPending;
        return new TerrainStaticPreparation(_visibleSprites, _visibleLights, true, candidateObjects, missingObjects);
    }

    private static bool IsUnlit(ItemsPakEntry? item) =>
        (item?.GraphicRenderFlags & UnlitGraphicFlag) != 0;

    private static int CompareSprites(TerrainStaticSprite left, TerrainStaticSprite right)
    {
        var queue = left.QueueIndex.CompareTo(right.QueueIndex);
        if (queue != 0)
            return queue;
        var tileDepth = left.TileDepth.CompareTo(right.TileDepth);
        if (tileDepth != 0)
            return tileDepth;
        var tileWorldY = left.TileWorldY.CompareTo(right.TileWorldY);
        if (tileWorldY != 0)
            return tileWorldY;
        var tileWorldX = left.TileWorldX.CompareTo(right.TileWorldX);
        if (tileWorldX != 0)
            return tileWorldX;
        var chainDepth = left.ChainDepth.CompareTo(right.ChainDepth);
        return chainDepth != 0 ? chainDepth : left.InsertionOrder.CompareTo(right.InsertionOrder);
    }

    private int EngineQueueIndex(StaticWorldObject staticObject)
    {
        var item = assets.GetItem(staticObject.TypeId);
        var graphicFlags = item?.GraphicRenderFlags ?? 0;
        var renderClass = item?.RenderClass ?? 0;
        if (renderClass == SpecialRenderClass)
        {
            if ((graphicFlags & FrontGraphicFlag) != 0)
                return 4;
            if ((graphicFlags & RearGraphicFlag) != 0)
                return 0;
            return 3;
        }

        if ((graphicFlags & RearGraphicFlag) != 0)
            return (staticObject.Flags & 0x20) != 0 || staticObject.SurfaceRenderLayer == 1 ? 0 : 2;
        return (graphicFlags & FrontGraphicFlag) != 0 ? 4 : 3;
    }
}

internal readonly record struct TerrainStaticPreparation(
    IReadOnlyList<TerrainStaticSprite> Sprites,
    IReadOnlyList<TerrainWorldLight> Lights,
    bool Changed,
    int CandidateObjects,
    int MissingObjects);
