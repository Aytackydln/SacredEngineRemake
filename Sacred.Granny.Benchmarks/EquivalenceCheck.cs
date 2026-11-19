using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Benchmarks.Legacy;

namespace Sacred.Granny.Benchmarks;

/// <summary>Whole-pose comparison of the optimised evaluator against the pre-optimisation copy.</summary>
internal static class EquivalenceCheck
{
    private const float Tolerance = 1e-4f;

    public static int Run()
    {
        var failures = 0;
        foreach (var (bones, scaled, nonUniform) in new[] { (32, 0f, false), (96, 0f, false), (96, 0.25f, false), (96, 0.25f, true), (96, 1f, true) })
        {
            var skeleton = SyntheticAnimation.CreateSkeleton(bones);
            var data = SyntheticAnimation.CreateSkinningData(skeleton);
            var clip = SyntheticAnimation.CreateClip("a", skeleton, 2f, 11, scaled, nonUniform);
            var other = SyntheticAnimation.CreateClip("b", skeleton, 1.5f, 12, scaled, nonUniform);
            var fresh = new GrnPoseEvaluator(data, clip);
            var legacy = new LegacyPoseEvaluator(data, clip);
            var random = new Random(9);
            float world = 0, regular = 0, rigid = 0;
            var time = 0f;
            for (var step = 0; step < 400; step++)
            {
                // Mostly forward playback, with occasional jumps backwards/forwards and past the clip end.
                time = random.Next(0, 10) == 0 ? (float)random.NextDouble() * 5f : time + 1f / 60f;
                GrnPose a, b;
                if (step % 3 == 0)
                {
                    var blend = new GrnPoseBlend(other, time * 0.7f, (float)random.NextDouble());
                    a = fresh.EvaluateBlended(time, GrnPoseTimeMode.Loop, blend);
                    b = legacy.EvaluateBlended(time, GrnPoseTimeMode.Loop, blend);
                }
                else if (step % 3 == 1) { a = fresh.EvaluateClamped(time); b = legacy.EvaluateClamped(time); }
                else { a = fresh.Evaluate(time); b = legacy.Evaluate(time); }
                world = Math.Max(world, Diff(a.WorldTransforms, b.WorldTransforms));
                regular = Math.Max(regular, Diff(a.RegularTransforms, b.RegularTransforms));
                rigid = Math.Max(rigid, Diff(a.RigidTransforms, b.RigidTransforms));
            }
            var ok = world <= Tolerance && regular <= Tolerance && rigid <= Tolerance;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} bones={bones,3} scaled={scaled:0.00} nonUniform={nonUniform,-5} " +
                              $"maxDiff world={world:E1} skin={regular:E1} rigid={rigid:E1}");
        }
        return failures == 0 ? 0 : 1;
    }

    private static float Diff(Matrix4x4[] x, Matrix4x4[] y)
    {
        float max = 0;
        for (var i = 0; i < x.Length; i++)
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 4; c++)
                {
                    var d = MathF.Abs(x[i][r, c] - y[i][r, c]);
                    if (!(d <= max)) max = d; // propagates NaN as a failure
                }
        return max;
    }
}
