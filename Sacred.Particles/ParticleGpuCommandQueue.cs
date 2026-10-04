using System.Collections.Immutable;
using System.Numerics;
using Sacred.Particles.Diagnostics;

namespace Sacred.Particles;

public readonly record struct ParticleControlHandle(long Identity, uint Generation);
public readonly record struct ParticleGpuBirth(int Slot, ParticleSimulationState State);

/// <summary>Frozen operation payload; no live CPU state/mirror is retained by the packet.</summary>
public sealed record ParticleGpuOperation
{
    public ParticleGpuOperation(ParticleGpuBirth[] births, float? step, Vector3 direction,
        ParticleGroundCollision collision, float groundHeight)
        : this((IEnumerable<ParticleGpuBirth>)births, step, direction, collision, groundHeight) { }
    public ParticleGpuOperation(IEnumerable<ParticleGpuBirth> births, float? step, Vector3 direction,
        ParticleGroundCollision collision, float groundHeight)
    {
        Births = ImmutableArray.CreateRange(births);
        Step = step; Direction = direction; Collision = collision; GroundHeight = groundHeight;
    }
    public ImmutableArray<ParticleGpuBirth> Births { get; }
    public float? Step { get; }
    public Vector3 Direction { get; }
    public ParticleGroundCollision Collision { get; }
    public float GroundHeight { get; }
    public ParticleControlHandle Instance { get; init; }
    public ulong Sequence { get; init; }
}

/// <summary>Ordered controls usable without a lifetime mirror or simulation arrays.
/// Draining transfers immutable packets once; a backend retains them until submission.</summary>
public sealed class ParticleGpuCommandQueue
{
    private static long _nextIdentity;
    private ulong _sequence;
    private readonly List<ParticleGpuBirth> _births = [];
    private readonly List<ParticleGpuOperation> _pending = [];
    public ParticleControlHandle Instance { get; private set; } = new(Interlocked.Increment(ref _nextIdentity), 1);
    public void AddBirth(int slot, ParticleSimulationState state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        _births.Add(new(slot, state));
    }
    public void Step(float dt, Vector3 direction, ParticleGroundCollision collision, float groundHeight)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.ControlPackets);
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Enum.IsDefined(collision)) throw new ArgumentOutOfRangeException(nameof(collision));
        FlushBirths();
        _pending.Add(Create(Array.Empty<ParticleGpuBirth>(), dt, direction, collision, groundHeight));
    }
    public ParticleGpuOperation[] TakePending()
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.ControlPackets);
        FlushBirths();
        if (_pending.Count == 0) return [];
        var result = _pending.ToArray();
        _pending.Clear();
        return result;
    }
    private ParticleGpuOperation Create(IEnumerable<ParticleGpuBirth> births, float? step,
        Vector3 direction, ParticleGroundCollision collision, float groundHeight)
        => new(births, step, direction, collision, groundHeight) { Instance = Instance, Sequence = checked(++_sequence) };
    private void FlushBirths()
    {
        if (_births.Count == 0) return;
        _pending.Add(Create(_births, null, default, default, 0));
        _births.Clear();
    }
    public void InvalidateRequests()
    {
        _births.Clear(); _pending.Clear();
        Instance = Instance with { Generation = checked(Instance.Generation + 1) };
    }
}
