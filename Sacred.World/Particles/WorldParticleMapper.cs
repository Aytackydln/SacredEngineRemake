using System.Globalization;
using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Maps Items.pak and Static.pak fields to world billboard/halo passes.</summary>
public static class WorldParticleMapper
{
    private const int MiniObjectAtlasSize = 256;
    private const float EngineTickDurationSeconds = 0.02f;
    private static readonly ParticleSpriteReference TorchFireSprite = new(
        "particle_fire02.tga",
        4,
        4,
        16,
        0.06f,
        ParticleShaderKind.ItemParticle);
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
        const string prefix = "MiniObjTex";
        var modelName = item.ModelDesc.ModelName;
        if (!modelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(
                modelName.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var atlasIndex))
        {
            return false;
        }

        if (frameCount > 0)
        {
            if (sourceXOrAtlasColumns == 0 ||
                sourceYOrAtlasRows == 0 ||
                frameDurationTicks == 0 ||
                frameCount > sourceXOrAtlasColumns * sourceYOrAtlasRows)
            {
                return false;
            }

            reference = new MiniObjectTextureReference(
                $"MINIOBJ{sourceXOrAtlasColumns}X{sourceYOrAtlasRows}_" +
                $"{frameCount}_{frameDurationTicks}_{atlasIndex}.TGA",
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

        if (sourceSize == 0 || MiniObjectAtlasSize % sourceSize != 0)
            return false;

        reference = new MiniObjectTextureReference(
            $"MINIOBJ{MiniObjectAtlasSize / sourceSize}_{atlasIndex}.TGA",
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
    /// Resolves a texture-free SimpleLight marker. These Items.pak entries have
    /// no mixed group or texture; their extent is the only authored size value.
    /// Colour is deliberately absent because it is not encoded by the marker.
    /// </summary>
    public static bool TryResolveProceduralHalo(
        ItemsPakEntry item,
        out ProceduralHaloReference reference)
    {
        reference = default;
        if (item.ModelDesc.ModelExtent == 0 ||
            item.MixedBaseGroupId != 0 ||
            !item.ModelDesc.ModelName.StartsWith("SimpleLight", StringComparison.OrdinalIgnoreCase) ||
            (item.GraphicRenderFlags & ItemsPakEntryModelDesc.UnlitGraphicFlag) == 0 ||
            !item.ModelDesc.UsesWorldLightRenderClass)
        {
            return false;
        }

        reference = new ProceduralHaloReference(
            item.ModelDesc.ModelExtent,
            ParticleShaderKind.ProceduralHalo);
        return true;
    }

    /// <summary>
    /// Resolves the authored blue world-light composites. Their stand/tree art
    /// remains a normal mixed sprite; this classification only selects the
    /// emissive alpha treatment needed by the glow and star pixels.
    /// </summary>
    public static bool TryResolveMixedLightEmitter(
        ItemsPakEntry item,
        out MixedLightEmitterReference reference)
    {
        reference = default;
        if (item.MixedBaseGroupId == 0 ||
            !item.ModelDesc.ModelName.StartsWith("LICHTER_", StringComparison.OrdinalIgnoreCase) ||
            !item.ModelDesc.UsesWorldLightRenderClass ||
            item.ModelDesc.ModelTransformFlags != 0x0100 ||
            item.ModelDesc.ModelExtent != 0 ||
            item.ModelDesc.TextureId != 0 ||
            item.EffectTextureId != 0 ||
            item.StaticSpriteFrameCount != 0)
        {
            return false;
        }

        reference = new MixedLightEmitterReference(
            item.MixedBaseGroupId,
            ParticleShaderKind.StaticAlphaSprite,
            ParticleShaderKind.ProceduralSparkle);
        return true;
    }

    /// <summary>
    /// Resolves particle sockets built into mixed world fixtures. Their flames
    /// are not present in the mixed.pak cutouts, so the original particle atlas
    /// is attached to the decoded fixture sprite.
    /// </summary>
    public static bool TryResolveFixtureEmitter(
        ItemsPakEntry item,
        out WorldParticleEmitterReference reference)
    {
        reference = default;
        if (item.MixedBaseGroupId == 0 ||
            item.ModelDesc.TextureId != 0 || item.EffectTextureId != 0)
        {
            return false;
        }

        if (item.ModelDesc.ModelName.Equals("DungeonA79", StringComparison.OrdinalIgnoreCase))
        {
            reference = new WorldParticleEmitterReference(
                TorchFireSprite,
                108.0f,
                45.0f,
                64.0f,
                64.0f,
                new Vector3(1.0f, 0.34f, 0.04f),
                150.0f,
                0.20f);
            return true;
        }

        if (item.ModelDesc.ModelName.Equals("Coalpot 1", StringComparison.OrdinalIgnoreCase))
        {
            reference = new WorldParticleEmitterReference(
                TorchFireSprite,
                -185.0f,
                -158.0f,
                215.0f,
                210.0f,
                new Vector3(1.0f, 0.24f, 0.03f),
                210.0f,
                0.22f,
                TransposeTexture: true);
            return true;
        }

        return false;
    }
}

public readonly record struct MiniObjectTextureReference(
    string TextureName,
    int SourceX,
    int SourceY,
    int SourceSize,
    int AtlasColumns,
    int AtlasRows,
    int FrameCount,
    float FrameDurationSeconds,
    ParticleShaderKind Shader);

public readonly record struct ProceduralHaloReference(
    ushort Extent,
    ParticleShaderKind Shader);

public readonly record struct MixedLightEmitterReference(
    uint MixedGroupId,
    ParticleShaderKind SpriteShader,
    ParticleShaderKind ParticleShader);
