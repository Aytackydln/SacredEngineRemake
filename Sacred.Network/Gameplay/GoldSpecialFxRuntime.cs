using System.Numerics;

namespace Sacred.Network.Gameplay;

/// <summary>Native FX/particle fields omitted from full 16F snapshots, and unresolved relocation/projection inputs.</summary>
public sealed record GoldSpecialFxRuntime
{
    public GoldObjectPosition? PositionRequest { get; init; }
    public ushort? PositionOpcode { get; init; }
    public GoldFxVectorBits? ParticlePosition { get; init; }
    public Vector3? ProjectedPositionInput { get; init; }
    public uint? ParticleClockWord { get; init; }
    public ushort? ParticleFlags { get; init; }
    public long Version { get; init; }
    public bool RelocationPending { get; init; }
}
