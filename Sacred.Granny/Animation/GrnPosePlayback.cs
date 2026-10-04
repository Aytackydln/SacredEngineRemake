using Sacred.Granny.Diagnostics;

namespace Sacred.Granny.Animation;

/// <summary>Owns source/version and submission order without evaluating animation data.
/// The default synchronous CPU sink preserves existing rendering and software tooling.</summary>
public sealed class GrnPosePlayback
{
    private static long _nextIdentity;
    private ulong _sequence;
    private ulong _sourceVersion = 1;
    internal GrnPosePlayback(GrnAnimatedMesh owner, GrnAnimationClip animation)
    {
        Instance = new(Interlocked.Increment(ref _nextIdentity), 1);
        Animation = animation;
        Cpu = new(owner);
        Sink = Cpu;
    }
    public GrnPoseInstanceHandle Instance { get; private set; }
    public GrnAnimationClip Animation { get; private set; }
    public GrnPoseRequest? LastRequest { get; private set; }
    public GrnCpuPoseRequestAdapter Cpu { get; }
    public IGrnPoseRequestSink Sink { get; set; }
    public void SetAnimation(GrnAnimationClip animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        Animation = animation;
        _sourceVersion = checked(_sourceVersion + 1);
    }
    public GrnPoseRequest Request(float seconds, GrnPoseTimeMode mode, bool materializeCpuVertices)
    {
        using var scope = AnimationPerformance.Measure(AnimationCpuStage.PoseRequests);
        var request = CreateRequest(seconds, mode, materializeCpuVertices);
        Sink.Submit(request);
        LastRequest = request;
        return request;
    }
    public GrnPose EvaluateCpu(float seconds, GrnPoseTimeMode mode)
    {
        var request = CreateRequest(seconds, mode, false);
        var pose = Cpu.Evaluate(request);
        LastRequest = request;
        return pose;
    }
    private GrnPoseRequest CreateRequest(float seconds, GrnPoseTimeMode mode, bool materialize)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var request = new GrnPoseRequest(Instance, checked(++_sequence), _sourceVersion,
            Animation, seconds, mode, materialize);
        return request;
    }
    /// <summary>Cancel deferred work when an owner is explicitly reset or its storage is recycled.</summary>
    public void InvalidateRequests()
    {
        Instance = Instance with { Generation = checked(Instance.Generation + 1) };
        _sourceVersion = checked(_sourceVersion + 1);
        LastRequest = null;
    }
}
