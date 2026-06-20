using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Shaders;

[StructLayout(LayoutKind.Sequential)]
public readonly struct ModelShaderTextureFlags(
    float textureMode,
    float animationValue,
    float painterDepth,
    float scaledAnimationTime)
{
    public const int FloatCount = 4;

    public readonly float TextureMode = textureMode;
    public readonly float AnimationValue = animationValue;
    public readonly float PainterDepth = painterDepth;
    public readonly float ScaledAnimationTime = scaledAnimationTime;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ModelShaderModelConstants(
    Matrix4x4 worldViewProjection,
    Matrix4x4 world,
    Vector4 modelColor,
    ModelShaderTextureFlags textureFlags)
{
    public const int FloatCount = 40;

    public readonly Matrix4x4 WorldViewProjection = worldViewProjection;
    public readonly Matrix4x4 World = world;
    public readonly Vector4 ModelColor = modelColor;
    public readonly ModelShaderTextureFlags TextureFlags = textureFlags;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ModelShaderSceneConstants(
    Vector4 lightPositionAndSpecularStrength,
    Vector4 cameraPositionAndShininess,
    Vector4 ambientColorAndIntensity,
    Vector4 lightColorAndDiffuseIntensity,
    Vector4 hdrDisplay)
{
    public const int FloatCount = 20;

    public readonly Vector4 LightPositionAndSpecularStrength = lightPositionAndSpecularStrength;
    public readonly Vector4 CameraPositionAndShininess = cameraPositionAndShininess;
    public readonly Vector4 AmbientColorAndIntensity = ambientColorAndIntensity;
    public readonly Vector4 LightColorAndDiffuseIntensity = lightColorAndDiffuseIntensity;
    public readonly Vector4 HdrDisplay = hdrDisplay;
}
