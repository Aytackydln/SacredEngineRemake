using System.Numerics;
using Sacred.Particles.Diagnostics;

namespace Sacred.Particles;

/// <summary>Compatibility capacity accounting, separate from backend commands.</summary>
internal sealed class ParticleCpuLifetimeMirror
{
    private struct Life { public float Fade, Size, Z, Vz, Gravity; }
    private readonly Life[] _life;
    private readonly List<int> _live = [];
    private readonly Stack<int> _free;
    public ParticleCpuLifetimeMirror(int capacity)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.SimulationStorage);
        _life = new Life[capacity];
        _free = new(Enumerable.Range(0, capacity).Reverse());
    }
    public int Count => _live.Count;
    public IReadOnlyList<int> LiveSlots => _live;
    public int Add(ParticleSimulationState state)
    {
        if (_free.Count == 0) throw new InvalidOperationException("Particle GPU batch capacity exceeded.");
        var slot = _free.Pop();
        _life[slot] = new() { Fade = state.Fade, Size = state.Size, Z = state.Position.Z,
            Vz = state.Velocity.Z, Gravity = state.Gravity };
        _live.Add(slot);
        return slot;
    }
    public void Update(SacredParticleParameterSet parameters, float dt, Vector3 direction,
        ParticleGroundCollision collision, float groundHeight)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.LifetimeMirror);
        var write = 0;
        for (var index = 0; index < _live.Count; index++)
        {
            var slot = _live[index];
            ref var life = ref _life[slot];
            if (!(life.Fade <= 0 || life.Size <= 0))
            {
                if (collision == ParticleGroundCollision.Die)
                {
                    life.Z += life.Vz * dt;
                    var acceleration = life.Gravity * dt;
                    life.Vz -= acceleration;
                    life.Vz += direction.Z * acceleration;
                    if (life.Z <= groundHeight) life.Fade = 0;
                    life.Gravity += parameters.Motion.GravityChangeRate * dt;
                }
                life.Fade += parameters.Motion.FadeChangeRate * dt;
                life.Size += parameters.Motion.SizeChangeRate * dt;
            }
            if (life.Fade <= 0 || life.Size <= 0) _free.Push(slot);
            else _live[write++] = slot;
        }
        if (write < _live.Count) _live.RemoveRange(write, _live.Count - write);
    }
}
