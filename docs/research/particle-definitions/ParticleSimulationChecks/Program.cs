using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Particles;
using Sacred.Particles.Particles;
using Sacred.World.Particles;

var checks = 0;
void Check(bool success, string message)
{
    if (!success) throw new Exception(message);
    checks++;
}
void Near(float actual, float expected, string label) =>
    Check(Math.Abs(actual - expected) <= 0.0002f, $"{label}: {actual} != {expected}");
float[] Values(JsonElement element) => element.EnumerateArray().Select(x => x.GetSingle()).ToArray();
ParticleSimulationState State(float[] values, int order = 0) => new()
{
    Position = new(values[0], values[1], values[2]), Velocity = new(values[6], values[7], values[8]),
    Gravity = values[9], Size = values[10], Fade = values[11], Rotation = values[12],
    AngularVelocity = values[14], DrawOrder = order, AtlasCell = order % 4
};
void Compare(ParticleSimulationState p, float[] v)
{
    Near(p.Position.X, v[0], "X"); Near(p.Position.Y, v[1], "Y"); Near(p.Position.Z, v[2], "Z");
    Near(p.Velocity.X, v[6], "Vx"); Near(p.Velocity.Y, v[7], "Vy"); Near(p.Velocity.Z, v[8], "Vz");
    Near(p.Gravity, v[9], "gravity"); Near(p.Size, v[10], "size"); Near(p.Fade, v[11], "fade");
    Near(p.Rotation, v[12], "rotation"); Near(p.AngularVelocity, v[14], "angular velocity");
}
using var reference = JsonDocument.Parse(File.ReadAllText(args.Length > 0 ? args[0] :
    "docs/research/particle-definitions/reports/particle-simulation-reference.json"));
var rates = Values(reference.RootElement.GetProperty("motion"));
var motion = MemoryMarshal.Read<SacredParticleMotionLayout>(MemoryMarshal.AsBytes(rates.AsSpan()));
var parameters = new SacredParticleParameterSet(0, default, motion);
var direction = motion.GravityDirection.Value;
foreach (var nativeCase in reference.RootElement.GetProperty("cases").EnumerateArray())
foreach (var mode in Enum.GetValues<ParticleSimulationMode>())
{
    var collision = Enum.Parse<ParticleGroundCollision>(nativeCase.GetProperty("mode").GetString()!);
    var ground = nativeCase.GetProperty("groundHeight").GetSingle();
    var state = State(Values(nativeCase.GetProperty("initial")));
    // Exercise full SIMD vectors and a scalar tail against the independently executed native code.
    var count = Vector<float>.Count * 2 + 1;
    var batch = new ParticleSimulationBatch(parameters, count);
    for (var i = 0; i < count; i++) batch.Add(state);
    foreach (var step in nativeCase.GetProperty("steps").EnumerateArray())
    {
        var dt = step.GetProperty("dt").GetSingle();
        var expected = Values(step.GetProperty("state"));
        // The native routine skips already dead particles at the next update.
        if (state.Fade <= 0 || state.Size <= 0) break;
        ParticleCpuSimulation.Advance(ref state, motion, dt, direction, collision: collision, groundHeight: ground);
        Compare(state, expected);
        batch.Update(dt, direction, mode, collision, ground);
        if (expected[11] <= 0 || expected[10] <= 0)
            Check(batch.Count == 0, "dead native particles were retained");
        else
            for (var i = 0; i < batch.Count; i++) Compare(batch[i], expected);
    }
}

// Stable compaction, metadata, zero-vector inward force, scalar tails and empty batches.
foreach (var mode in Enum.GetValues<ParticleSimulationMode>())
foreach (var count in new[] { 0, 1, Vector<float>.Count - 1, Vector<float>.Count, Vector<float>.Count + 1, 200 })
{
    var batch = new ParticleSimulationBatch(parameters, 200);
    for (var i = 0; i < count; i++) batch.Add(new() { Size = i % 3 == 0 ? -1 : 5, Fade = 255, DrawOrder = i, AtlasCell = i % 4 });
    batch.Update(0, direction, mode);
    var survivors = Enumerable.Range(0, count).Where(i => i % 3 != 0).ToArray();
    Check(batch.Count == survivors.Length, "compaction count");
    for (var i = 0; i < batch.Count; i++)
    {
        Check(batch[i].DrawOrder == survivors[i] && batch[i].AtlasCell == survivors[i] % 4, "compaction metadata");
        Check(float.IsFinite(batch[i].Velocity.X), "zero-vector force generated NaN");
    }
}

// Real decoded emitters: preserve births, capacity, colors, wind, variants and depth while switching modes.
foreach (var quality in Enum.GetValues<SacredParticleQuality>())
{
    var catalogue = SacredParticleCatalogue.LoadEmbedded(quality);
    foreach (var definition in catalogue.Definitions.Where(d => d.Status == SacredParticleDefinitionStatus.Decoded))
    {
        var placement = new WorldParticleScriptPlacement(123,
            new SacredScriptCreateObject(null, definition.TypeId, new(0, 0, 0), null, 26), definition, 0, 0);
        var scalar = new WorldParticleEmitter(placement, catalogue.WorldUnitsPerTile, catalogue.Projection)
            { SimulationMode = ParticleSimulationMode.CpuScalar };
        var simd = new WorldParticleEmitter(placement, catalogue.WorldUnitsPerTile, catalogue.Projection);
        var left = new List<WorldParticle>(); var right = new List<WorldParticle>();
        for (var frame = 0; frame < 150; frame++)
        {
            left.Clear(); right.Clear();
            // Switch without resetting live state halfway through.
            if (frame == 75) simd.SimulationMode = ParticleSimulationMode.CpuScalar;
            scalar.Update(0.03125f, left); simd.Update(0.03125f, right);
            Check(left.Count == right.Count, "emitter liveness diverged");
            for (var i = 0; i < left.Count; i++)
            {
                Near(left[i].WorldX, right[i].WorldX, "emitter X"); Near(left[i].WorldY, right[i].WorldY, "emitter Y");
                Near(left[i].Height, right[i].Height, "emitter height"); Near(left[i].Size, right[i].Size, "emitter size");
                Near(left[i].Rotation, right[i].Rotation, "emitter rotation");
                Check(left[i].Color == right[i].Color && left[i].AtlasCell == right[i].AtlasCell &&
                    left[i].DrawOrder == right[i].DrawOrder && left[i].Additive == right[i].Additive,
                    "emitter color/atlas/order/blend diverged");
            }
        }
    }
}
foreach (var mode in Enum.GetValues<ParticleSimulationMode>())
{
    var batch = new ParticleSimulationBatch(new(0, default, default), 200);
    for (var i = 0; i < 200; i++) batch.Add(new() { Fade = 255, Size = 5 });
    for (var i = 0; i < 100; i++) batch.Update(0.01f, Vector3.Zero, mode);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++) batch.Update(0.01f, Vector3.Zero, mode);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0, $"{mode} updates allocated {allocated} bytes");
}
Console.WriteLine($"Passed {checks:N0} particle simulation checks; hardware SIMD={Vector.IsHardwareAccelerated}, lanes={Vector<float>.Count}.");

// Opt-in microbenchmark measures integration/compaction only, not end-to-end frame rendering.
if (args.Contains("--benchmark"))
{
    foreach (var capacity in new[] { 100, 200, 8192 })
    foreach (var mode in Enum.GetValues<ParticleSimulationMode>())
    {
        var batch = new ParticleSimulationBatch(new(0, default, default), capacity);
        for (var i = 0; i < capacity; i++) batch.Add(new() { Position = new(1, 2, 3), Velocity = new(1, 2, 3), Size = 5, Fade = 255 });
        for (var i = 0; i < 10000; i++) batch.Update(0.001f, Vector3.Zero, mode);
        var samples = new double[5];
        var iterations = 10_000_000 / capacity;
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++) batch.Update(0.001f, Vector3.Zero, mode);
            samples[sample] = timer.Elapsed.TotalNanoseconds / (iterations * capacity);
        }
        Array.Sort(samples);
        Console.WriteLine($"{mode}, {capacity} particles: median {samples[2]:0.00} ns/particle/update");
    }
}
