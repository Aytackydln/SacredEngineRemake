using Sacred.Granny.Animation;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Shared asynchronous cache for the original FX helper animations.</summary>
public sealed class WorldFxAnimations
{
    private readonly Dictionary<string, Task<GrnAnimationClip?>> _loads = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, Task<GrnAnimationClip?>>? Load { get; set; }

    public GrnAnimationClip? Find(string name)
    {
        if (!_loads.TryGetValue(name, out var task))
        {
            if (Load is null) return null;
            _loads[name] = task = Load(name);
            Console.WriteLine($"Loading FX animation: {name}.");
        }
        if (!task.IsCompleted) return null;
        if (task.IsFaulted) throw new InvalidDataException($"Cannot load FX animation {name}.", task.Exception);
        return task.GetAwaiter().GetResult() ?? throw new InvalidDataException($"FX model {name} has no default animation.");
    }

    public float? Lifetime(SacredParticleDefinition definition)
    {
        if (definition.VampireTransformation is not { } transformation) return null;
        var clip = Find(transformation.Animation);
        if (clip is null) return null;
        // Native stops births at the animation's end, then waits for the particle vector to drain.
        var drain = definition.ParameterSets.Max(p => MathF.Min(
            p.Motion.FadeChangeRate < 0 ? 255 / -p.Motion.FadeChangeRate : float.PositiveInfinity,
            p.Motion.SizeChangeRate < 0 ? (p.Emission.Size + MathF.Abs(p.Emission.SizeRandomWidth)) / -p.Motion.SizeChangeRate : float.PositiveInfinity));
        return clip.DurationSeconds + drain + 1f / 60;
    }
}
