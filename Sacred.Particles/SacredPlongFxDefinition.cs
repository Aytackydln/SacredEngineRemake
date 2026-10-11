using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredPlongFxDefinition
{
    public SacredPlongFxStateLayout InitialState { get; init; }
    public float RandomScale { get; init; }
    public float SpawnWidth { get; init; }
    public float SpawnOrigin { get; init; }
    public float SpawnHeight { get; init; }
    public float VelocityAngle { get; init; }
    public float HorizontalSpeed { get; init; }
    public float VerticalSpeedWidth { get; init; }
    public float VerticalSpeedOrigin { get; init; }
    public float InitialHalfSize { get; init; }
    public float FullTurn { get; init; }
    public float Gravity { get; init; }
    public float BounceFactor { get; init; }
    public float ShrinkRate { get; init; }
}
