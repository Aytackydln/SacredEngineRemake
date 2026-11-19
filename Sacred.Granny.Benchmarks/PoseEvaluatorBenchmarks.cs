using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Sacred.Granny.Animation;
using Sacred.Granny.Benchmarks.Legacy;

namespace Sacred.Granny.Benchmarks;

/// <summary>One full pose evaluation (sampling + hierarchy + skin/rigid palettes), advancing time each call
/// like playback does. Legacy is the pre-optimisation evaluator copied from git.</summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PoseEvaluatorBenchmarks
{
    private const float Step = 1f / 60f;
    private GrnPoseEvaluator _new = null!;
    private LegacyPoseEvaluator _legacy = null!;
    private GrnAnimationClip _blendSource = null!;
    private float _time, _sourceTime;

    [Params(32, 96)] public int Bones { get; set; }
    /// <summary>Fraction of bones with an animated non-identity scale channel.</summary>
    [Params(0f, 0.25f)] public float ScaledBones { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var skeleton = SyntheticAnimation.CreateSkeleton(Bones);
        var data = SyntheticAnimation.CreateSkinningData(skeleton);
        var clip = SyntheticAnimation.CreateClip("walk", skeleton, 2f, 11, ScaledBones);
        _blendSource = SyntheticAnimation.CreateClip("idle", skeleton, 1.5f, 12, ScaledBones);
        _new = new GrnPoseEvaluator(data, clip);
        _legacy = new LegacyPoseEvaluator(data, clip);
    }

    [BenchmarkCategory("Evaluate"), Benchmark(Baseline = true)]
    public ulong Evaluate_Legacy() => _legacy.Evaluate(_time += Step).Revision;

    [BenchmarkCategory("Evaluate"), Benchmark]
    public ulong Evaluate_Optimized() => _new.Evaluate(_time += Step).Revision;

    [BenchmarkCategory("Blend"), Benchmark(Baseline = true)]
    public ulong Blend_Legacy() => _legacy.EvaluateBlended(_time += Step, GrnPoseTimeMode.Loop,
        new GrnPoseBlend(_blendSource, _sourceTime += Step, 0.4f)).Revision;

    [BenchmarkCategory("Blend"), Benchmark]
    public ulong Blend_Optimized() => _new.EvaluateBlended(_time += Step, GrnPoseTimeMode.Loop,
        new GrnPoseBlend(_blendSource, _sourceTime += Step, 0.4f)).Revision;
}

/// <summary>A dense-area frame: many independent actors, each advanced one tick and evaluated.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CrowdFrameBenchmarks
{
    private const float Step = 1f / 60f;
    private const int ActorCount = 200;
    private GrnPoseEvaluator[] _new = [];
    private LegacyPoseEvaluator[] _legacy = [];
    private float[] _times = [];

    [Params(64)] public int Bones { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var skeleton = SyntheticAnimation.CreateSkeleton(Bones);
        var data = SyntheticAnimation.CreateSkinningData(skeleton);
        // A handful of shared clips, like creatures sharing authored motions.
        var clips = Enumerable.Range(0, 4).Select(i => SyntheticAnimation.CreateClip($"clip{i}", skeleton, 1f + i * .5f, 20 + i, 0.1f)).ToArray();
        _new = new GrnPoseEvaluator[ActorCount];
        _legacy = new LegacyPoseEvaluator[ActorCount];
        _times = new float[ActorCount];
        var random = new Random(5);
        for (var i = 0; i < ActorCount; i++)
        {
            _new[i] = new(data, clips[i % clips.Length]);
            _legacy[i] = new(data, clips[i % clips.Length]);
            _times[i] = (float)random.NextDouble() * 2f;
        }
    }

    /// <summary>Reported per actor; multiply by actor count for the frame cost.</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = ActorCount)]
    public ulong Frame_Legacy()
    {
        ulong sum = 0;
        for (var i = 0; i < _legacy.Length; i++) sum += _legacy[i].Evaluate(_times[i] += Step).Revision;
        return sum;
    }

    [Benchmark(OperationsPerInvoke = ActorCount)]
    public ulong Frame_Optimized()
    {
        ulong sum = 0;
        for (var i = 0; i < _new.Length; i++) sum += _new[i].Evaluate(_times[i] += Step).Revision;
        return sum;
    }
}
