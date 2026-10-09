using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Authored constants and initial state recovered from the shared teleport
/// constructor, start, updater and serializer. CastSpeed is a caller event input.</summary>
public sealed record SacredTeleportParticleDefinition(
    SacredTeleportParticleStateLayout InitialState,
    float MaximumStep, float RandomScale, float InitialPhaseRange, float FullAngle,
    float OrbitRadius, float PhaseSpeed, float OrbitSpeedMinimum, float OrbitSpeedWidth,
    float OrbitEnergy, float BaseHeight, float HeightRate, float AngleRate,
    int EjectionDivisor, float EjectionSpeed, float NormalizeThreshold,
    float Gravity, float BounceMultiplier, int FlashDivisor, int FlashWidth, int FlashMinimum,
    float ReturnWait, uint NativeAddress);
