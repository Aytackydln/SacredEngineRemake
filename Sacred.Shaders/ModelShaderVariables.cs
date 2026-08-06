using System.Numerics;

namespace Sacred.Shaders;

public static class ModelShaderVariables
{
    public const float TextureModeNoTexture = 0.0f;
    public const float TextureModeBaseTexture = 1.0f;
    public const float TextureModeMultiTextureFill = 3.0f;

    public const float TextureAnimationNone = 1.0f;
    public const float TextureAnimationScrollBlackKey = 1.5f;
    public const float TextureAnimationRadialSweepBlackKey = 1.75f;

    public static Vector4 ColorFromName(string name)
    {
        var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name);
        return new Vector4(
            0.35f + (hash & 0xFF) / 255.0f * 0.55f,
            0.35f + ((hash >> 8) & 0xFF) / 255.0f * 0.55f,
            0.35f + ((hash >> 16) & 0xFF) / 255.0f * 0.55f,
            1.0f);
    }

    public static float PackTextureMode(bool hasTexture, bool hasOverlay, bool multiTextureFill)
    {
        if (!hasTexture)
            return TextureModeNoTexture;

        if (!hasOverlay)
            return TextureModeBaseTexture;

        return multiTextureFill ? TextureModeMultiTextureFill : TextureModeBaseTexture;
    }

    public static float PackTextureAnimation(bool isAnimated, bool radialSweepBlackKey, bool overlay)
    {
        if (!isAnimated)
            return TextureAnimationNone;

        var value = radialSweepBlackKey
            ? TextureAnimationRadialSweepBlackKey
            : TextureAnimationScrollBlackKey;
        return overlay ? -value : value;
    }

}

/// <summary>Serializes model and scene values in their matching HLSL constant-buffer order.</summary>
public sealed class ModelShaderConstantsUpdater
{
    public unsafe void WriteModelBase(
        float* target,
        Matrix4x4 worldViewProjection,
        Matrix4x4 world,
        Vector4 modelColor) =>
        WriteModelBase(
            target,
            new ModelShaderModelConstants(
                worldViewProjection,
                world,
                modelColor,
                new ModelShaderTextureFlags(
                    ModelShaderVariables.TextureModeNoTexture,
                    ModelShaderVariables.TextureAnimationNone,
                    ModelShaderLayout.PreserveProjectedDepth,
                    scaledAnimationTime: 0.0f)));

    public unsafe void WriteModelBase(float* target, ModelShaderModelConstants constants)
    {
        WriteMatrix(constants.WorldViewProjection, target);
        WriteMatrix(constants.World, target + 16);
        WriteVector4(constants.ModelColor, target + 32);
    }

    public unsafe void WriteModelColor(float* target, Vector4 modelColor) =>
        WriteVector4(modelColor, target);

    public unsafe void WriteTextureFlags(
        float* target,
        float textureMode,
        float animationValue,
        float painterDepth,
        float scaledAnimationTime)
    {
        WriteTextureFlags(
            target,
            new ModelShaderTextureFlags(textureMode, animationValue, painterDepth, scaledAnimationTime));
    }

    public unsafe void WriteTextureFlags(float* target, ModelShaderTextureFlags flags)
    {
        target[0] = flags.TextureMode;
        target[1] = flags.AnimationValue;
        target[2] = flags.PainterDepth;
        target[3] = flags.ScaledAnimationTime;
    }

    public unsafe void WriteSceneConstants(
        float* target,
        Vector3 lightPosition,
        float specularIntensity,
        Vector3 cameraPosition,
        float shininess,
        Vector4 ambientColorAndIntensity,
        Vector4 lightColorAndDiffuseIntensity,
        Vector4 hdrDisplay) =>
        WriteSceneConstants(
            target,
            new ModelShaderSceneConstants(
                new Vector4(lightPosition, Math.Max(0.0f, specularIntensity)),
                new Vector4(cameraPosition, Math.Max(1.0f, shininess)),
                ambientColorAndIntensity with { W = Math.Max(0.0f, ambientColorAndIntensity.W) },
                lightColorAndDiffuseIntensity with { W = Math.Max(0.0f, lightColorAndDiffuseIntensity.W) },
                new Vector4(
                    Math.Max(0.0f, hdrDisplay.X),
                    Math.Max(0.0f, hdrDisplay.Y),
                    Math.Max(0.0f, hdrDisplay.Z),
                    Math.Max(0.0f, hdrDisplay.W))));

    public unsafe void WriteSceneConstants(float* target, ModelShaderSceneConstants constants)
    {
        WriteVector4(constants.LightPositionAndSpecularStrength, target);
        WriteVector4(constants.CameraPositionAndShininess, target + 4);
        WriteVector4(constants.AmbientColorAndIntensity, target + 8);
        WriteVector4(constants.LightColorAndDiffuseIntensity, target + 12);
        WriteVector4(constants.HdrDisplay, target + 16);
    }

    private static unsafe void WriteMatrix(Matrix4x4 matrix, float* target)
    {
        target[0] = matrix.M11;
        target[1] = matrix.M12;
        target[2] = matrix.M13;
        target[3] = matrix.M14;
        target[4] = matrix.M21;
        target[5] = matrix.M22;
        target[6] = matrix.M23;
        target[7] = matrix.M24;
        target[8] = matrix.M31;
        target[9] = matrix.M32;
        target[10] = matrix.M33;
        target[11] = matrix.M34;
        target[12] = matrix.M41;
        target[13] = matrix.M42;
        target[14] = matrix.M43;
        target[15] = matrix.M44;
    }

    private static unsafe void WriteVector4(Vector4 value, float* target)
    {
        target[0] = value.X;
        target[1] = value.Y;
        target[2] = value.Z;
        target[3] = value.W;
    }
}
