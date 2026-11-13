using Sacred.Granny.Meshes;

namespace Sacred.Granny.Animation;

/// <summary>Single in-flight evaluator. Only the submitting thread publishes completed palettes.</summary>
internal sealed class GrnAsyncPoseEvaluator
{
    private readonly GrnPoseEvaluator _evaluator;
    private readonly Mesh _bindMesh;
    private Mesh? _skinned;
    private Task<GrnPose>? _pending;
    private GrnPoseRequest _request;
    private static int _updatesPerSecond = 60;
    private static float _intervalSeconds = 1f / 60;
    public static float IntervalSeconds => Volatile.Read(ref _intervalSeconds);

    public GrnAsyncPoseEvaluator(Mesh bindMesh, GrnSkinningData data, GrnAnimationClip animation)
    {
        _bindMesh = bindMesh;
        _evaluator = new(data, animation);
    }

    public static int UpdatesPerSecond
    {
        get => Volatile.Read(ref _updatesPerSecond);
        set
        {
            if (value is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _updatesPerSecond, value);
            Volatile.Write(ref _intervalSeconds, 1f / value);
        }
    }

    public ulong Submit(GrnAnimatedMesh owner, in GrnPoseRequest request, ulong evaluatedSequence)
    {
        evaluatedSequence = PublishCompleted(owner, evaluatedSequence);
        if (_pending is not null) return evaluatedSequence;
        // The caller owns cadence in simulation time. A second wall-clock gate
        // here drops due updates, especially when frame timing has small jitter.
        if (_request.Sequence == evaluatedSequence && evaluatedSequence != 0 && SameSample(_request, request))
        {
            _request = request;
            return request.Sequence;
        }
        _request = request;
        // Copy the value request: workers never access mutable playback/renderer state.
        var work = request;
        _pending = Task.Run(() =>
        {
            if (!ReferenceEquals(_evaluator.Animation, work.Animation)) _evaluator.SetAnimation(work.Animation);
            var pose = work.Blend is { } blend ? _evaluator.EvaluateBlended(work.TimeSeconds, work.TimeMode, blend)
                : work.TimeMode == GrnPoseTimeMode.Clamp ? _evaluator.EvaluateClamped(work.TimeSeconds)
                : _evaluator.Evaluate(work.TimeSeconds);
            if (work.MaterializeCpuVertices)
                GrnCpuSkinner.Materialize(pose, _skinned ??= _bindMesh.CreateInstance());
            return pose;
        });
        return evaluatedSequence;
    }

    public ulong PublishCompleted(GrnAnimatedMesh owner, ulong evaluatedSequence)
    {
        if (_pending is { IsCompleted: true } completed)
        {
            _pending = null;
            var pose = completed.GetAwaiter().GetResult();
            if (_request.Instance == owner.Playback.Instance && _request.SourceVersion == owner.Playback.SourceVersion &&
                _request.Sequence > evaluatedSequence)
            {
                owner.PublishPose(pose, _request.MaterializeCpuVertices ? _skinned : null);
                evaluatedSequence = _request.Sequence;
            }
        }
        return evaluatedSequence;
    }

    private static bool SameSample(in GrnPoseRequest previous, in GrnPoseRequest next) =>
        previous.Instance == next.Instance && previous.SourceVersion == next.SourceVersion &&
        ReferenceEquals(previous.Animation, next.Animation) && previous.TimeMode == next.TimeMode &&
        previous.MaterializeCpuVertices == next.MaterializeCpuVertices && previous.Blend == next.Blend &&
        SampleTime(previous) == SampleTime(next);

    private static float SampleTime(in GrnPoseRequest request) => request.TimeMode == GrnPoseTimeMode.Clamp
        ? Math.Clamp(request.TimeSeconds, 0, MathF.Max(0, request.Animation.DurationSeconds))
        : GrnAnimationSampler.WrapTime(request.TimeSeconds, request.Animation.DurationSeconds);
}
