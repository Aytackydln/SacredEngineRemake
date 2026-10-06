using System;
using System.Numerics;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;

namespace Sacred.Engine.Graphics.Sprites;

internal static class Dx12PlayerOcclusionProbeFactory
{
    private const float PainterDepthScale = 1.0f / 4096.0f;

    public static PlayerOcclusionProbe Create(
        SacredCamera camera,
        SceneModel? playerModel,
        int renderWidth,
        int renderHeight)
    {
        if (playerModel is null)
            return default;

        var clip = Vector4.Transform(
            new Vector4(playerModel.OcclusionProbeCenter, 1.0f),
            camera.View * camera.Projection);
        var inverseW = MathF.Abs(clip.W) > float.Epsilon ? 1.0f / clip.W : 1.0f;
        var screenPosition = new Vector2(
            (clip.X * inverseW * 0.5f + 0.5f) * renderWidth,
            (0.5f - clip.Y * inverseW * 0.5f) * renderHeight);
        var depthKey = WorldModelDepth.FromPosition(playerModel.OcclusionProbeCenter);
        var centerDepthKey = WorldPainterDepth.FromWorld(camera.ViewCenter);
        var painterDepth = Math.Clamp(
            0.50f - (depthKey - centerDepthKey) * PainterDepthScale,
            0.20f,
            0.72f);
        return new PlayerOcclusionProbe(screenPosition, painterDepth, true);
    }
}

internal readonly record struct PlayerOcclusionProbe(
    Vector2 ScreenPosition,
    float SceneDepth,
    bool IsActive);
