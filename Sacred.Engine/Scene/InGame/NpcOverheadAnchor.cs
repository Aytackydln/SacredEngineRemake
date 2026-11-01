using System.Numerics;
using Sacred.Engine.Graphics.Skinning;

namespace Sacred.Engine.Scene.InGame;

/// <summary>The native billboard attaches to the current Bip01 Head palette, independently of actor facing.</summary>
internal static class NpcOverheadAnchor
{
    public static bool TryProject(SceneModel model, SacredCamera camera, int width, int height, out Vector2 screen)
    {
        screen = default;
        var animation = model.Geometry.Animation;
        if (animation is null || !animation.Pose.Data.TryFindBone("Bip01 Head", out var head)) return false;
        // Rendering consumes the published CPU pose, even when a newer request is pending.
        // GPU-only sampling needs separate query storage: mutating the draw pose here
        // invalidates uploaded palettes and CPU vertices in the middle of a frame.
        var pose = animation.Playback.Sink is GpuPoseRequestSink && animation.Playback.LastRequest is { } request
            ? animation.Playback.Cpu.EvaluateSnapshot(request)
            : animation.Pose;
        if (!pose.IsEvaluated) return false;
        var local = pose.Data.Projection.Project(pose.BoneWorldTransforms[head].Translation);
        var clip = Vector4.Transform(new Vector4(local, 1), model.Transform * camera.View * camera.Projection);
        if (clip.W <= 0 || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y)) return false;
        screen = new((clip.X / clip.W + 1) * width * .5f, (1 - clip.Y / clip.W) * height * .5f);
        return screen.X >= -64 && screen.X < width + 64 && screen.Y >= -64 && screen.Y < height + 128;
    }
}

