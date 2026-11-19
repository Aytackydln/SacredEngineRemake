using Sacred.Granny.Animation;

namespace Sacred.GameplayHost.Networking;

/// <summary>Actual shared pose backend for the restored physical root. A new
/// control starts at zero; the saved previous-control start time is discarded
/// by the original 44FC50 activation/play sequence. World translation, saved
/// creature normalization and admission readiness remain separate.</summary>
public sealed class WorldHostHeroAnimation
{
    private readonly GrnAnimatedMesh _mesh;
    internal WorldHostHeroAnimation(WorldHostHeroModelResources resources, float loops, double speed)
    {
        if (resources.IsInvalidated || resources.Body.Mesh is null || resources.Body.Skin is null ||
            !float.IsFinite(loops) || loops <= 0 || !double.IsFinite(speed) || speed <= 0)
            throw new NotSupportedException("Host animation requires current original resources and positive finite compared control parameters.");
        _mesh = new(resources.Body.Mesh, resources.Body.Skin, resources.SavedAnimation) { MaterializeCpuVertices = false };
        LoopCount = loops;
        PlaybackSpeed = speed;
        _mesh.EvaluatePose(0);
    }

    public float LoopCount { get; }
    public double PlaybackSpeed { get; }
    public GrnPose Pose => IsInvalidated ? throw new InvalidOperationException("Host animation lifetime ended.") : _mesh.Pose;
    public GrnPoseInstanceHandle Instance => _mesh.Playback.Instance;
    public bool IsInvalidated { get; private set; }

    internal void Invalidate()
    {
        _mesh.Playback.InvalidateRequests();
        IsInvalidated = true;
    }
}
