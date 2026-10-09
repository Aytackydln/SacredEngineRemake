using System.Numerics;

namespace Sacred.Particles;

/// <summary>Standard native parameter sampling shared by point and line births.</summary>
public static class ParticleBirthStateFactory
{
    public static ParticleSimulationState Create(SacredParticleParameterSet parameters, SacredParticleDefinition definition,
        SeededParticleRandom random, Vector3 position, float elapsed, Vector3 gravityDirection, int drawOrder)
    {
        var e = parameters.Emission;
        float Scalar(float center, float width) => center + ((float)random.NextDouble() * 2 - 1) * width;
        Vector3 Vector(Vector3 center, Vector3 width) => new(Scalar(center.X, width.X),
            Scalar(center.Y, width.Y), Scalar(center.Z, width.Z));
        var particle = new ParticleSimulationState
        {
            Position = Vector(position, e.PositionRandomWidth.Value),
            Velocity = Vector(e.Velocity.Value, e.VelocityRandomWidth.Value),
            Gravity = Scalar(e.Gravity, e.GravityRandomWidth), Size = Scalar(e.Size, e.SizeRandomWidth),
            Rotation = Scalar(e.Rotation, e.RotationRandomWidth),
            AngularVelocity = Scalar(e.AngularVelocity, e.AngularVelocityRandomWidth),
            AtlasCell = e.VariantSelection switch { 0 => 0, 255 => parameters.Index, _ => random.Next(e.VariantSelection) },
            Fade = 255, DrawOrder = drawOrder
        };
        particle.Color = definition.Draw!.UsesParticleColor
            ? SacredParticleColor.Randomize(e.Color, e.ColorRandomWidth, random.NextDouble) : e.Color;
        ParticleCpuSimulation.Advance(ref particle, parameters.Motion, elapsed, gravityDirection, applyInwardAcceleration: false);
        return particle;
    }
}
