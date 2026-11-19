using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Sacred.Granny.Animation;
using Sacred.Granny.Benchmarks.Legacy;

namespace Sacred.Granny.Benchmarks;

/// <summary>Per-channel micro benchmarks. Time advances monotonically across calls, as in playback.</summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SamplerBenchmarks
{
    private const float Step = 1f / 60f;
    private float[] _times = [];
    private Vector3[] _vectors = [];
    private Quaternion[] _rotations = [];
    private float _time, _end;
    private int _hint;
    private Matrix4x4 _rigid, _scaled;

    [Params(8, 64)] public int Keys { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(3);
        _times = new float[Keys];
        _vectors = new Vector3[Keys];
        _rotations = new Quaternion[Keys];
        for (var i = 0; i < Keys; i++)
        {
            _times[i] = i / 30f;
            _vectors[i] = SyntheticAnimation.RandomVector(random);
            _rotations[i] = SyntheticAnimation.RandomQuaternion(random);
        }
        _end = _times[^1];
        _rigid = Matrix4x4.CreateFromQuaternion(_rotations[0]) * Matrix4x4.CreateTranslation(1, 2, 3);
        _scaled = Matrix4x4.CreateScale(1.3f) * _rigid;
    }

    private float NextTime() { _time += Step; if (_time > _end) { _time = 0; _hint = 0; } return _time; }

    [BenchmarkCategory("Quaternion"), Benchmark(Baseline = true)]
    public Quaternion Quaternion_Legacy() => LegacySampler.SampleQuaternion(_times, _rotations, NextTime(), Quaternion.Identity);

    [BenchmarkCategory("Quaternion"), Benchmark]
    public Quaternion Quaternion_Optimized() => GrnAnimationSampler.SampleQuaternion(_times, _rotations, NextTime(), Quaternion.Identity, ref _hint);

    [BenchmarkCategory("Vector3"), Benchmark(Baseline = true)]
    public Vector3 Vector3_Legacy() => LegacySampler.SampleVector3(_times, _vectors, NextTime(), Vector3.Zero);

    [BenchmarkCategory("Vector3"), Benchmark]
    public Vector3 Vector3_Optimized() => GrnAnimationSampler.SampleVector3(_times, _vectors, NextTime(), Vector3.Zero, ref _hint);

    [BenchmarkCategory("CreateTransform"), Benchmark(Baseline = true)]
    public Matrix4x4 CreateTransform_Legacy() => LegacySampler.CreateTransform(_vectors[1], _rotations[1], Matrix4x4.Identity);

    [BenchmarkCategory("CreateTransform"), Benchmark]
    public Matrix4x4 CreateTransform_Optimized() => GrnAnimationSampler.CreateTransform(_vectors[1], _rotations[1], Matrix4x4.Identity);

    [BenchmarkCategory("Rigid (unscaled bone)"), Benchmark(Baseline = true)]
    public Matrix4x4 RigidUnscaled_Legacy() => LegacyRigid.CreateOrOriginal(_rigid);

    [BenchmarkCategory("Rigid (unscaled bone)"), Benchmark]
    public Matrix4x4 RigidUnscaled_Optimized() => GrnRigidTransform.CreateOrOriginal(_rigid);

    [BenchmarkCategory("Rigid (scaled bone)"), Benchmark(Baseline = true)]
    public Matrix4x4 RigidScaled_Legacy() => LegacyRigid.CreateOrOriginal(_scaled);

    [BenchmarkCategory("Rigid (scaled bone)"), Benchmark]
    public Matrix4x4 RigidScaled_Optimized() => GrnRigidTransform.CreateOrOriginal(_scaled);
}
