using System;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;

namespace Sacred.Engine.Graphics.Sprites;

internal static class Dx12PlayerOcclusionProbeFactory
{
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private const float PlayerDepthBias = 0.0005f;

    public static PlayerOcclusionProbe Create(
        SacredCamera camera,
        SceneModel? playerModel,
        int renderWidth,
        int renderHeight)
    {
        if (playerModel is null)
            return default;

        var clip = Vector4.Transform(
            new Vector4(playerModel.VisualCenter, 1.0f),
            camera.View * camera.Projection);
        var inverseW = MathF.Abs(clip.W) > float.Epsilon ? 1.0f / clip.W : 1.0f;
        var screenPosition = new Vector2(
            (clip.X * inverseW * 0.5f + 0.5f) * renderWidth,
            (0.5f - clip.Y * inverseW * 0.5f) * renderHeight);
        var depthKey = WorldPainterDepth.FromWorld(playerModel.DepthAnchor);
        var centerDepthKey = WorldPainterDepth.FromWorld(camera.WorldCenter);
        var painterDepth = Math.Clamp(
            0.50f - (depthKey - centerDepthKey) * PainterDepthScale,
            0.20f,
            0.72f);
        var sceneDepth = Math.Clamp(painterDepth + PlayerDepthBias, 0.0f, 1.0f);
        return new PlayerOcclusionProbe(screenPosition, sceneDepth, true);
    }
}

internal readonly record struct PlayerOcclusionProbe(
    Vector2 ScreenPosition,
    float SceneDepth,
    bool IsActive);
