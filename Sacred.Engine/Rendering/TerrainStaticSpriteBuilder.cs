using System;
using System.Collections.Generic;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.World.Particles;

namespace Sacred.Engine.Rendering;

internal sealed class TerrainStaticSpriteBuilder(AssetManager assets)
{
    private const uint NormalRenderExcludeFlags = 0x290;
    private const uint NightOnlyObjectFlag = 0x00000040;
    private const int ExteriorActiveLayer = 1;
    private const byte SpecialRenderClass = 0x0C;
    private const uint RearGraphicFlag = 0x00000004;
    private const uint FrontGraphicFlag = 0x00800000;
    private const float ObjectShiftX = 47.8f;
    private const float ObjectShiftY = -0.3f;
    private const float LightHaloDiameterScale = 1.2f;
    private static readonly System.Numerics.Vector3 DefaultProceduralHaloColour =
        new(1.0f, 0.64f, 0.24f);
    private const float ProceduralHaloOpacity = 0.12f;

    private readonly List<TerrainStaticSprite> _visibleSprites = new(1024);
    private readonly List<TerrainWorldLight> _visibleLights = new(64);
    private readonly MixedLightAppearanceCache _mixedLightAppearanceCache = new();
    private bool _assetRequestsPending = true;
    private bool _nightObjectsVisible;
    private string? _lastParticleSummary;

    public bool HasPendingAssetRequests => _assetRequestsPending;

    public TerrainStaticPreparation Prepare(
        IReadOnlyList<Sector> sectors,
        bool worldChanged,
        IndoorTileGroup? activeIndoorGroup,
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
        var animatedSpriteCount = 0;
        var fixtureParticleEmitterCount = 0;
        var mixedLightEmitterCount = 0;
        var proceduralHaloCount = 0;
        var requestsPending = false;
        foreach (var sector in sectors)
        {
            candidateObjects += sector.StaticObjects.Count;
            foreach (var staticObject in sector.StaticObjects.Objects)
            {
                if ((staticObject.Flags & NormalRenderExcludeFlags) != 0)
                {
                    continue;
                }

                var footX = staticObject.ProjectedX + ObjectShiftX;
                var footY = staticObject.ProjectedY + ObjectShiftY;
                var item = assets.GetItem(staticObject.TypeId);
                MiniObjectTextureReference miniObjectReference = default;
                var isAnimatedMiniObject = item is { } miniObjectItem &&
                                         WorldParticleMapper.TryResolveMiniObject(
                                             miniObjectItem,
                                             staticObject.MiniObjectSourceXOrAtlasColumns,
                                             staticObject.MiniObjectSourceYOrAtlasRows,
                                             staticObject.MiniObjectSourceSize,
                                             staticObject.MiniObjectFrameDurationTicks,
                                             staticObject.MiniObjectFrameCount,
                                         out miniObjectReference) &&
                                         miniObjectReference.FrameCount > 1;
                if (!IsVisibleOnSurface(staticObject, activeIndoorGroup))
                    continue;

                if (item is { } haloItem &&
                    WorldParticleMapper.TryResolveProceduralHalo(
                        haloItem,
                        out var haloReference))
                {
                    var diameter = haloReference.Extent * LightHaloDiameterScale;
                    _visibleLights.Add(new TerrainWorldLight(
                        footX - diameter * 0.5f,
                        footY - diameter * 0.5f,
                        diameter,
                        DefaultProceduralHaloColour,
                        ProceduralHaloOpacity,
                        WorldLightShape.RadialHalo));
                    proceduralHaloCount++;
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

                var isMixedLightEmitter = item is { } mixedLightItem &&
                                          WorldParticleMapper.TryResolveMixedLightEmitter(
                                              mixedLightItem,
                                              out _);

                var renderWidth = sprite.Width;
                var renderHeight = sprite.Height;
                var spriteIsoX = footX - sprite.AnchorX;
                var spriteIsoY = footY - sprite.AnchorY;
                if (Math.Abs(spriteIsoX) > 1048576 || Math.Abs(spriteIsoY) > 1048576)
                {
                    missingObjects++;
                    continue;
                }

                if ((staticObject.Flags & NightOnlyObjectFlag) != 0 &&
                    !nightObjectsVisible &&
                    sprite.FrameCount <= 1)
                    continue;

                if (isMixedLightEmitter &&
                         _mixedLightAppearanceCache.TryGet(sprite, out var lightAppearance))
                {
                    _visibleLights.Add(new TerrainWorldLight(
                        spriteIsoX + lightAppearance.CenterX - lightAppearance.Diameter * 0.5f,
                        spriteIsoY + lightAppearance.CenterY - lightAppearance.Diameter * 0.5f,
                        lightAppearance.Diameter,
                        lightAppearance.Colour,
                        lightAppearance.Opacity,
                        WorldLightShape.RadialHalo));
                    var sparkleDiameter = lightAppearance.SparkleDiameter;
                    _visibleLights.Add(new TerrainWorldLight(
                        spriteIsoX + lightAppearance.CenterX - sparkleDiameter * 0.5f,
                        spriteIsoY + lightAppearance.EmitterTop - sparkleDiameter * 0.72f,
                        sparkleDiameter,
                        new System.Numerics.Vector3(0.68f, 0.76f, 1.0f),
                        0.72f,
                        WorldLightShape.SparkleCluster));
                }

                if (item is { } fixtureItem &&
                    WorldParticleMapper.TryResolveFixtureEmitter(fixtureItem, out var fixtureEmitter))
                {
                    if (TryAddParticleEmitter(
                            fixtureEmitter,
                            spriteIsoX,
                            spriteIsoY,
                            staticObject,
                            out var pending))
                    {
                        fixtureParticleEmitterCount++;
                    }
                    requestsPending |= pending;
                }

                _visibleSprites.Add(new TerrainStaticSprite(
                    sprite,
                    IsUnlit(item),
                    false,
                    isMixedLightEmitter,
                    false,
                    renderWidth,
                    renderHeight,
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
                if (isAnimatedMiniObject)
                    animatedSpriteCount++;
                if (isMixedLightEmitter)
                    mixedLightEmitterCount++;
            }
        }

        _visibleSprites.Sort(CompareSprites);
        _assetRequestsPending = requestsPending;
        if (!requestsPending)
            LogParticleSummary(
                animatedSpriteCount,
                fixtureParticleEmitterCount,
                mixedLightEmitterCount,
                proceduralHaloCount);
        return new TerrainStaticPreparation(_visibleSprites, _visibleLights, true, candidateObjects, missingObjects);
    }

    private bool TryAddParticleEmitter(
        WorldParticleEmitterReference reference,
        float anchorX,
        float anchorY,
        StaticWorldObject source,
        out bool requestPending)
    {
        requestPending = false;
        if (!assets.TryGetWorldParticleSpriteOrRequest(reference.Sprite, out var particleSprite))
        {
            requestPending = true;
            return false;
        }

        if (particleSprite is null)
            return false;

        var particleX = anchorX + reference.OffsetX;
        var particleY = anchorY + reference.OffsetY;
        _visibleSprites.Add(new TerrainStaticSprite(
            particleSprite,
            true,
            true,
            false,
            reference.TransposeTexture,
            reference.Width,
            reference.Height,
            particleX,
            particleY,
            anchorX,
            anchorY,
            source.SurfaceRenderLayer,
            EngineQueueIndex(source) + 1,
            source.TileDepth,
            source.TileWorldY,
            source.TileWorldX,
            source.ChainDepth,
            source.InsertionOrder));

        var lightCenterX = particleX + reference.Width * 0.5f;
        var lightCenterY = particleY + reference.Height * 0.5f;
        _visibleLights.Add(new TerrainWorldLight(
            lightCenterX - reference.LightDiameter * 0.5f,
            lightCenterY - reference.LightDiameter * 0.5f,
            reference.LightDiameter,
            reference.LightColour,
            reference.LightOpacity,
            WorldLightShape.RadialHalo));
        return true;
    }

    private void LogParticleSummary(
        int animatedSprites,
        int fixtureParticleEmitters,
        int mixedLightEmitters,
        int proceduralHalos)
    {
        var summary = $"World effects ready: animated static sprites={animatedSprites}, " +
                      $"fixture particle emitters={fixtureParticleEmitters}, " +
                      $"mixed light emitters={mixedLightEmitters}, " +
                      $"authored light markers={proceduralHalos}.";
        if (summary == _lastParticleSummary)
            return;

        _lastParticleSummary = summary;
        Console.WriteLine(summary);
    }

    private static bool IsUnlit(ItemsPakEntry? item) =>
        (item?.GraphicRenderFlags & ItemsPakEntryModelDesc.UnlitGraphicFlag) != 0;

    private static bool IsVisibleOnSurface(
        StaticWorldObject staticObject,
        IndoorTileGroup? activeIndoorGroup)
    {
        if (activeIndoorGroup is null)
            return staticObject.SurfaceRenderLayer <= ExteriorActiveLayer;

        var belongsToActiveSection = activeIndoorGroup.ContainsWorldTile(
            staticObject.TileWorldX,
            staticObject.TileWorldY);
        if (staticObject.SurfaceRenderLayer > ExteriorActiveLayer)
        {
            return belongsToActiveSection &&
                   staticObject.SurfaceRenderLayer == activeIndoorGroup.SurfaceRenderLayer;
        }

        return !belongsToActiveSection ||
               staticObject.SurfaceRenderLayer != ExteriorActiveLayer ||
               !staticObject.UsesAlternateSurface;
    }

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
