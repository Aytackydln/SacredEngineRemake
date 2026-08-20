using Sacred.Core.Pak.Items;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Maps Items.pak and Static.pak fields to world billboard/halo passes.</summary>
public static class WorldParticleMapper
{
    private const int MiniObjectAtlasSize = 256;
    private const float EngineTickDurationSeconds = 0.02f;

    public static bool TryResolveMiniObject(
        ItemsPakEntry item,
        byte sourceXOrAtlasColumns,
        byte sourceYOrAtlasRows,
        byte sourceSize,
        byte frameDurationTicks,
        byte frameCount,
        out MiniObjectTextureReference reference)
    {
        reference = default;
        var descriptor = item.ModelDesc;
        if (!descriptor.UsesMiniObjectTexture)
            return false;

        if (frameCount > 0)
        {
            if (!descriptor.UsesAnimatedMiniObjectRenderClass ||
                sourceXOrAtlasColumns == 0 ||
                sourceYOrAtlasRows == 0 ||
                frameDurationTicks == 0 ||
                frameCount > sourceXOrAtlasColumns * sourceYOrAtlasRows)
            {
                return false;
            }

            reference = new MiniObjectTextureReference(
                descriptor.MiniObjectTextureId,
                0,
                0,
                0,
                sourceXOrAtlasColumns,
                sourceYOrAtlasRows,
                frameCount,
                frameDurationTicks * EngineTickDurationSeconds,
                ParticleShaderKind.StaticAlphaSprite);
            return true;
        }

        if (!descriptor.UsesStaticMiniObjectRenderClass ||
            sourceSize == 0 ||
            MiniObjectAtlasSize % sourceSize != 0)
        {
            return false;
        }

        reference = new MiniObjectTextureReference(
            descriptor.MiniObjectTextureId,
            sourceXOrAtlasColumns,
            sourceYOrAtlasRows,
            sourceSize,
            0,
            0,
            0,
            0.0f,
            ParticleShaderKind.StaticAlphaSprite);
        return true;
    }

    /// <summary>
    /// Resolves the visible halo carried by an animated mini-object. Items.pak
    /// supplies the unlit/render-class flags and extent; Static.pak supplies the
    /// atlas animation parameters.
    /// </summary>
    public static bool TryResolveAnimatedSpriteHalo(
        ItemsPakEntry item,
        MiniObjectTextureReference sprite,
        out AnimatedSpriteHaloReference reference)
    {
        reference = default;
        if (!item.ModelDesc.EmitsAnimatedSpriteHalo || sprite.FrameCount <= 1)
            return false;

        reference = new AnimatedSpriteHaloReference(
            item.ModelDesc.ModelExtent,
            ParticleShaderKind.ProceduralHalo);
        return true;
    }

    /// <summary>
    /// Resolves an invisible authored illumination volume. Rendering actual
    /// world illumination is deliberately separate from the visible halo pass.
    /// </summary>
    public static bool TryResolveWorldLightMarker(
        ItemsPakEntry item,
        out WorldLightMarkerReference reference)
    {
        reference = default;
        if (!item.ModelDesc.IsWorldLightMarker)
            return false;

        reference = new WorldLightMarkerReference(item.ModelDesc.ModelExtent);
        return true;
    }

    /// <summary>
    /// Selects mixed sprites whose numeric Items.pak fields permit embedded
    /// emission. Decoded sprite pixels make the final decision in the renderer.
    /// </summary>
    public static bool TryResolveMixedLightEmitter(
        ItemsPakEntry item,
        out MixedLightEmitterReference reference)
    {
        reference = default;
        if (!item.ModelDesc.MayContainMixedSpriteEmission)
            return false;

        reference = new MixedLightEmitterReference(
            item.MixedBaseGroupId,
            ParticleShaderKind.StaticAlphaSprite,
            ParticleShaderKind.ProceduralSparkle);
        return true;
    }
}

public readonly record struct MiniObjectTextureReference(
    uint TextureId,
    int SourceX,
    int SourceY,
    int SourceSize,
    int AtlasColumns,
    int AtlasRows,
    int FrameCount,
    float FrameDurationSeconds,
    ParticleShaderKind Shader);

public readonly record struct AnimatedSpriteHaloReference(
    ushort Extent,
    ParticleShaderKind Shader);

public readonly record struct WorldLightMarkerReference(ushort Radius);

public readonly record struct MixedLightEmitterReference(
    uint MixedGroupId,
    ParticleShaderKind SpriteShader,
    ParticleShaderKind ParticleShader);
