using System.Numerics;

namespace Sacred.Particles;

public interface IParticleGpuBackend
{
    bool IsAvailable { get; }
    ParticleSimulationState[] Snapshot(ParticleGpuBatch batch);
}

/// <summary>Compatibility CPU capacity mirror plus independent backend commands.
/// Full positions/velocities remain in the renderer's persistent GPU buffer.</summary>
public sealed class ParticleGpuBatch
{
    private readonly ParticleCpuLifetimeMirror _mirror;
    public ParticleGpuBatch(SacredParticleParameterSet parameters, int capacity)
    {
        Parameters = parameters;
        Capacity = capacity;
        _mirror = new(capacity);
    }
    public SacredParticleParameterSet Parameters { get; }
    public int Capacity { get; }
    public ParticleGpuCommandQueue Commands { get; } = new();
    public int Count => _mirror.Count;
    public IReadOnlyList<int> LiveSlots => _mirror.LiveSlots;
    public void Add(ParticleSimulationState state) => Commands.AddBirth(_mirror.Add(state), state);
    public void Update(float dt, Vector3 direction, ParticleGroundCollision collision, float groundHeight)
    {
        Commands.Step(dt, direction, collision, groundHeight);
        _mirror.Update(Parameters, dt, direction, collision, groundHeight);
    }
    public ParticleGpuOperation[] TakePending() => Commands.TakePending();
}
