using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Scene;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Phase 8 opt-in. Models with unmigrated synchronous consumers retain their CPU owner.</summary>
internal sealed class GpuPoseBackendSelection
{
    private readonly Dictionary<GrnPosePlayback, string> _reported = new(ReferenceEqualityComparer.Instance);
    public bool Enabled { get; set; }
    public int GpuCount { get; private set; }
    public int CpuCount { get; private set; }
    public void Apply(SceneState scene, bool pipelinesReady, string? failure, bool skinningEnabled)
    {
        GpuCount = CpuCount = 0;
        var reasons = new Dictionary<GrnPosePlayback, string?>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < scene.Models.Count; i++)
        {
            var model = scene.Models[i];
            if (model.Geometry.Animation is not { } animation) continue;
            var playback = animation.Playback;
            var reason = !Enabled ? "explicit CPU pose selection" :
                !skinningEnabled ? "CPU skeletal drawing selected" :
                !pipelinesReady ? failure ?? "GPU pose pipelines pending" :
                i == 0 ? "player attachments/model seeds await phases 9-10" :
                model.EquipmentEffects is not null ? "equipment consumers await phases 9-10" :
                HasSweep(model) ? "surface query consumer awaits phase 9" : null;
            if (!reasons.TryGetValue(playback, out var previousReason) || previousReason is null)
                reasons[playback] = reason;
        }
        foreach (var model in scene.Models)
        {
            if (model.Geometry.Animation is not { } animation) continue;
            var playback = animation.Playback;
            var reason = reasons[playback]; // Any CPU consumer keeps a shared pose owner on CPU.
            if (reason is null)
            {
                playback.Sink = GpuPoseRequestSink.Instance;
                animation.MaterializeCpuVertices = false; GpuCount++;
            }
            else
            {
                if (playback.Sink is GpuPoseRequestSink && playback.LastRequest is { } request)
                    playback.Cpu.Evaluate(request with { MaterializeCpuVertices = false });
                playback.Sink = playback.Cpu; CpuCount++;
            }
            var status = reason ?? "GPU sampling/hierarchy/palettes; coarse diagnostic anchor; visibility fail-open until phase 9";
            if (!_reported.TryGetValue(playback, out var prior) || prior != status)
            { _reported[playback] = status; EngineLog.WriteLine($"Pose backend: {model.Name}: {status}."); }
        }
        var removed = new List<GrnPosePlayback>();
        foreach (var owner in _reported.Keys) if (!reasons.ContainsKey(owner)) removed.Add(owner);
        foreach (var owner in removed) _reported.Remove(owner);
    }
    private static bool HasSweep(SceneModel model)
    {
        if (model.TextureAliases is null) return false;
        foreach (var texture in model.TextureAliases.Values)
            if (texture.Animation.Mode == TextureAnimationMode.RadialSweepBlackKey ||
                texture.OverlayAnimation.Mode == TextureAnimationMode.RadialSweepBlackKey) return true;
        return false;
    }
}
