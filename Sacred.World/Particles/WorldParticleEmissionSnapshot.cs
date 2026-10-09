using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Explicit transition state; never collected during normal GPU playback.</summary>
public sealed record WorldParticleEmissionSnapshot(int[] RandomState, float Elapsed, int NextDrawOrder,
    ParticleSimulationState[][] Particles)
{
    /// <summary>Complete GPU slot/list state for explicit inspection or GPU continuation.</summary>
    public byte[]? GpuState { get; init; }
    /// <summary>Preserves native point bursts that have not reached their first update.</summary>
    public bool BurstPending { get; init; }
    public float Age { get; init; }
    public bool NativeRetired { get; init; }
    public bool NativeEmissionInitialized { get; init; }
    public bool MovementReady { get; init; }
    public float NativeReleaseTime { get; init; }
    public float OrbitAngle { get; init; }
    public Vector3? ReleasedPoint { get; init; }
    public float CycleCountdown { get; init; }
    public uint CycleRandomState { get; init; }
    public bool CycleBurst { get; init; }
    public ParticleEmissionLine? CurrentLine { get; init; }
    public ParticleEmissionLine? ReleasedLine { get; init; }
}

public interface IWorldParticleEmissionBackend
{
    bool IsAvailable { get; }
    bool IsReady(WorldGpuEmitter emitter) => IsAvailable;
    bool HasState(WorldGpuEmitter emitter) => IsAvailable;
    WorldParticleEmissionSnapshot Snapshot(WorldGpuEmitter emitter);
}
