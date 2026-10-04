using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Explicit transition state; never collected during normal GPU playback.</summary>
public sealed record WorldParticleEmissionSnapshot(int[] RandomState, float Elapsed, int NextDrawOrder,
    ParticleSimulationState[][] Particles)
{
    /// <summary>Complete GPU slot/list state for explicit inspection or GPU continuation.</summary>
    public byte[]? GpuState { get; init; }
}

public interface IWorldParticleEmissionBackend
{
    bool IsAvailable { get; }
    bool IsReady(WorldGpuEmitter emitter) => IsAvailable;
    bool HasState(WorldGpuEmitter emitter) => IsAvailable;
    WorldParticleEmissionSnapshot Snapshot(WorldGpuEmitter emitter);
}
