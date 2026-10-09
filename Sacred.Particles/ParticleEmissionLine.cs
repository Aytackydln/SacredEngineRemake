using System.Numerics;

namespace Sacred.Particles;

/// <summary>Caller-supplied local attachment positions for a native line emitter.</summary>
public readonly record struct ParticleEmissionLine(Vector3 Start, Vector3 End)
{
    public bool IsFinite => float.IsFinite(Start.X) && float.IsFinite(Start.Y) && float.IsFinite(Start.Z) &&
                            float.IsFinite(End.X) && float.IsFinite(End.Y) && float.IsFinite(End.Z);
}
