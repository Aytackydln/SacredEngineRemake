using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

/// <summary>The native billboard attaches to the current Bip01 Head palette, independently of actor facing.</summary>
internal static class NpcOverheadAnchor
{
    public static bool TryProject(SceneModel model, SacredCamera camera, int width, int height, out Vector2 screen)
    {
        screen = default;
        var animation = model.Geometry.Animation;
        if (animation is null || !animation.Pose.Data.TryFindBone("Bip01 Head", out var head)) return false;
        // A GPU pose has no current CPU palette. Query bones without materializing or skinning vertices.
        if (animation.Playback.LastRequest is { } request && animation.Playback.Cpu.EvaluatedSequence != request.Sequence)
            animation.Playback.Cpu.Evaluate(request with { MaterializeCpuVertices = false });
        var pose = animation.Pose;
        if (!pose.IsEvaluated) return false;
        var local = pose.Data.Projection.Project(pose.BoneWorldTransforms[head].Translation);
        var clip = Vector4.Transform(new Vector4(local, 1), model.Transform * camera.View * camera.Projection);
        if (clip.W <= 0 || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y)) return false;
        screen = new((clip.X / clip.W + 1) * width * .5f, (1 - clip.Y / clip.W) * height * .5f);
        return screen.X >= -64 && screen.X < width + 64 && screen.Y >= -64 && screen.Y < height + 128;
    }
}
