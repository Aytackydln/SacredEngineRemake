using System;
using System.Collections.Generic;
using System.Numerics;
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
    private const float AnimatedLightDiameterScale = 1.0f;
    private const float AnimatedSurfaceLightDiameterScale = 2.0f;
    private const float LargeUnlitMixedLightRadius = 480.0f;
    private const float AuthoredLightOpacity = 0.48f;
    private static readonly Vector3 AuthoredLightColour = Vector3.One;

    private readonly List<TerrainStaticSprite> _visibleSprites = new(1024);
    private readonly List<TerrainWorldLight> _visibleLights = new(64);
    private readonly AnimatedSpriteHaloAppearanceCache _animatedSpriteHaloAppearanceCache = new();
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
        var animatedSpriteHaloCount = 0;
        var mixedLightEmitterCount = 0;
        var worldLightMarkerCount = 0;
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
                    WorldParticleMapper.TryResolveWorldLightMarker(
                        haloItem,
                        out var lightMarker))
                {
                    var diameter = lightMarker.Radius * 2.0f;
                    _visibleLights.Add(new TerrainWorldLight(
                        footX - diameter * 0.5f,
                        footY - diameter * 0.5f,
                        diameter,
                        AuthoredLightColour,
                        AuthoredLightOpacity,
                        WorldLightShape.SurfaceIllumination));
                    worldLightMarkerCount++;
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

                if (item is { } animatedHaloItem &&
                    WorldParticleMapper.TryResolveAnimatedSpriteHalo(
                        animatedHaloItem,
                        miniObjectReference,
                        out var haloReference) &&
                    _animatedSpriteHaloAppearanceCache.TryGet(sprite, out var haloAppearance))
                {
                    var diameter = haloReference.Extent * AnimatedLightDiameterScale;
                    _visibleLights.Add(new TerrainWorldLight(
                        spriteIsoX + haloAppearance.CenterX - diameter * 0.5f,
                        spriteIsoY + haloAppearance.CenterY - diameter * 0.5f,
                        diameter,
                        haloAppearance.Colour,
                        AnimatedSpriteHaloAppearanceCache.HaloOpacity,
                        WorldLightShape.RadialHalo));
                    var surfaceDiameter = haloReference.Extent * AnimatedSurfaceLightDiameterScale;
                    _visibleLights.Add(new TerrainWorldLight(
                        footX - surfaceDiameter * 0.5f,
                        footY - surfaceDiameter * 0.5f,
                        surfaceDiameter,
                        Vector3.One,
                        AuthoredLightOpacity,
                        WorldLightShape.SurfaceIllumination));
                    animatedSpriteHaloCount++;
                }

                MixedLightAppearance lightAppearance = default;
                var isMixedLightEmitter = item is { } mixedLightItem &&
                                          WorldParticleMapper.TryResolveMixedLightEmitter(
                                              mixedLightItem,
                                              out _) &&
                                          _mixedLightAppearanceCache.TryGet(
                                              sprite,
                                              out lightAppearance);
                if (isMixedLightEmitter)
                {
                    var surfaceRadius = lightAppearance.SurfaceLightRadius;
                    if (item!.Value.ModelDesc.HasExtendedMixedSpriteGraphicFlag)
                        surfaceRadius = MathF.Max(surfaceRadius, LargeUnlitMixedLightRadius);
                    var surfaceDiameter = surfaceRadius * 2.0f;
                    _visibleLights.Add(new TerrainWorldLight(
                        footX - surfaceDiameter * 0.5f,
                        footY - surfaceDiameter * 0.5f,
                        surfaceDiameter,
                        Vector3.One,
                        lightAppearance.SurfaceLightOpacity,
                        WorldLightShape.SurfaceIllumination));
                    _visibleLights.Add(new TerrainWorldLight(
                        spriteIsoX + lightAppearance.CenterX - lightAppearance.LocalHaloDiameter * 0.5f,
                        spriteIsoY + lightAppearance.CenterY - lightAppearance.LocalHaloDiameter * 0.5f,
                        lightAppearance.LocalHaloDiameter,
                        lightAppearance.Colour,
                        lightAppearance.LocalHaloOpacity,
                        WorldLightShape.RadialHalo));
                    if (lightAppearance.HasSparkles)
                    {
                        var sparkleDiameter = lightAppearance.SparkleDiameter;
                        _visibleLights.Add(new TerrainWorldLight(
                            spriteIsoX + lightAppearance.CenterX - sparkleDiameter * 0.5f,
                            spriteIsoY + lightAppearance.EmitterTop - sparkleDiameter * 0.72f,
                            sparkleDiameter,
                            new Vector3(0.68f, 0.76f, 1.0f),
                            0.72f,
                            WorldLightShape.SparkleCluster));
                    }
                }

                _visibleSprites.Add(new TerrainStaticSprite(
                    sprite,
                    false,
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
                animatedSpriteHaloCount,
                mixedLightEmitterCount,
                worldLightMarkerCount);
        return new TerrainStaticPreparation(_visibleSprites, _visibleLights, true, candidateObjects, missingObjects);
    }

    private void LogParticleSummary(
        int animatedSprites,
        int animatedSpriteHalos,
        int mixedLightEmitters,
        int worldLightMarkers)
    {
        var summary = $"World effects ready: animated static sprites={animatedSprites}, " +
                      $"animated sprite halos={animatedSpriteHalos}, " +
                      $"mixed light emitters={mixedLightEmitters}, " +
                      $"authored light markers={worldLightMarkers}.";
        if (summary == _lastParticleSummary)
            return;

        _lastParticleSummary = summary;
        EngineLog.WriteLine(summary);
    }

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
