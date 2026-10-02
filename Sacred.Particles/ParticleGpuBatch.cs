using System.Numerics;

namespace Sacred.Particles;

public interface IParticleGpuBackend
{
    bool IsAvailable { get; }
    ParticleSimulationState[] Snapshot(ParticleGpuBatch batch);
}

public readonly record struct ParticleGpuBirth(int Slot, ParticleSimulationState State);
public sealed record ParticleGpuOperation(ParticleGpuBirth[] Births, float? Step,
    Vector3 Direction, ParticleGroundCollision Collision, float GroundHeight);

/// <summary>CPU births and capacity accounting; positions/velocities live in the renderer's persistent GPU buffer.</summary>
public sealed class ParticleGpuBatch(SacredParticleParameterSet parameters, int capacity)
{
    private struct Life
    {
        public float Fade, Size, Z, Vz, Gravity;
    }
    private readonly Life[] _life = new Life[capacity];
    private readonly List<int> _live = [];
    private readonly Stack<int> _free = new(Enumerable.Range(0, capacity).Reverse());
    private readonly List<ParticleGpuBirth> _births = [];
    private readonly List<ParticleGpuOperation> _pending = [];
    public SacredParticleParameterSet Parameters { get; } = parameters;
    public int Capacity { get; } = capacity;
    public int Count => _live.Count;
    public IReadOnlyList<int> LiveSlots => _live;

    public void Add(ParticleSimulationState state)
    {
        if (_free.Count == 0) throw new InvalidOperationException("Particle GPU batch capacity exceeded.");
        var slot = _free.Pop();
        _life[slot] = new() { Fade = state.Fade, Size = state.Size, Z = state.Position.Z,
            Vz = state.Velocity.Z, Gravity = state.Gravity };
        _live.Add(slot);
        _births.Add(new(slot, state));
    }

    public void Update(float dt, Vector3 direction, ParticleGroundCollision collision, float groundHeight)
    {
        FlushBirths();
        _pending.Add(new([], dt, direction, collision, groundHeight));
        var write = 0;
        for (var index = 0; index < _live.Count; index++)
        {
            var slot = _live[index];
            ref var life = ref _life[slot];
            if (!(life.Fade <= 0 || life.Size <= 0))
            {
                // Preserve native capacity/random-birth decisions without GPU count readbacks.
                // Die collision needs only the vertical component; other movement stays entirely on GPU.
                if (collision == ParticleGroundCollision.Die)
                {
                    life.Z += life.Vz * dt;
                    var acceleration = life.Gravity * dt;
                    life.Vz -= acceleration;
                    life.Vz += direction.Z * acceleration;
                    if (life.Z <= groundHeight) life.Fade = 0;
                    life.Gravity += Parameters.Motion.GravityChangeRate * dt;
                }
                life.Fade += Parameters.Motion.FadeChangeRate * dt;
                life.Size += Parameters.Motion.SizeChangeRate * dt;
            }
            if (life.Fade <= 0 || life.Size <= 0) _free.Push(slot);
            else _live[write++] = slot;
        }
        if (write < _live.Count) _live.RemoveRange(write, _live.Count - write);
    }

    public ParticleGpuOperation[] TakePending()
    {
        FlushBirths();
        var operations = _pending.ToArray();
        _pending.Clear();
        return operations;
    }

    private void FlushBirths()
    {
        if (_births.Count == 0) return;
        _pending.Add(new(_births.ToArray(), null, default, default, 0));
        _births.Clear();
    }
}
