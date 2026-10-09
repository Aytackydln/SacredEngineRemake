using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Manual update 0x771410; float stores mirror the native x87 store boundaries.</summary>
internal static class TeleportParticleMovement
{
    public static void Initialize(ref SacredTeleportParticleLayout p,
        SacredTeleportParticleDefinition d, Func<int> random)
    {
        p.VerticalPhase = (float)(random() * (double)d.RandomScale * d.InitialPhaseRange);
        p.OrbitAngle = 0;
        p.OrbitRadius = d.OrbitRadius;
        p.PhaseSpeed = d.PhaseSpeed;
        var speed = (float)(random() * (double)d.RandomScale * d.OrbitSpeedWidth + d.OrbitSpeedMinimum);
        p.OrbitSpeed = (random() % 2 == 0 ? -1 : 1) * speed;
        p.Velocity.Z = 0;
        p.HalfSize = p.Energy = d.OrbitEnergy;
        p.Rotation = (float)(random() * (double)d.RandomScale * d.FullAngle);
        p.Frame = p.ParameterSetId = 0;
    }

    public static void Advance(ref SacredTeleportParticleLayout p, SacredTeleportParticleStateLayout state,
        SacredTeleportParticleDefinition d, float dt, Func<int> random)
    {
        if (p.Energy == d.OrbitEnergy)
        {
            p.Position = OrbitPosition(p, state, d);
            var angle = p.OrbitAngle + (double)dt * p.OrbitSpeed;
            if (angle > state.EnvelopeAngle) angle -= state.EnvelopeAngle;
            else if (angle < 0) angle += state.EnvelopeAngle;
            p.OrbitAngle = (float)angle;
            var phase = p.VerticalPhase + (double)dt * p.PhaseSpeed;
            if (phase > d.FullAngle) phase -= d.FullAngle;
            p.VerticalPhase = (float)phase;
            if (random() % d.EjectionDivisor == 0) Eject(ref p, state, d, dt, random);
            return;
        }
        var z = p.Position.Z + (double)dt * p.Velocity.Z;
        p.Position = new((float)(p.Position.X + (double)dt * p.Velocity.X),
            (float)(p.Position.Y + (double)dt * p.Velocity.Y), (float)z);
        p.Velocity.Z = (float)(p.Velocity.Z - (double)dt * d.Gravity);
        if (z < 0) p.Velocity.Z = (float)((double)p.Velocity.Z * d.BounceMultiplier);
        p.HalfSize = random() % d.FlashDivisor == 0
            ? (float)((double)p.Energy + random() % d.FlashWidth + d.FlashMinimum) : p.Energy;
        p.Energy = (float)(p.Energy - 2 * (double)dt);
        if (p.HalfSize <= 0) Initialize(ref p, d, random);
    }

    private static Vector3 OrbitPosition(SacredTeleportParticleLayout p,
        SacredTeleportParticleStateLayout state, SacredTeleportParticleDefinition d) => new(
        (float)(Math.Cos(p.OrbitAngle) * p.OrbitRadius),
        (float)(Math.Sin(p.OrbitAngle) * p.OrbitRadius),
        (float)(Math.Sin(p.VerticalPhase) * state.EnvelopeHeight + d.BaseHeight));

    private static void Eject(ref SacredTeleportParticleLayout p, SacredTeleportParticleStateLayout state,
        SacredTeleportParticleDefinition d, float dt, Func<int> random)
    {
        p.Position = OrbitPosition(p, state, d);
        p.Velocity = p.Position;
        var length = Math.Sqrt((double)p.Velocity.X * p.Velocity.X +
            (double)p.Velocity.Y * p.Velocity.Y + (double)p.Velocity.Z * p.Velocity.Z);
        if (length < d.NormalizeThreshold) p.Velocity = Vector3.Zero;
        else
        {
            var inverse = 1 / length;
            p.Velocity = new((float)(p.Velocity.X * inverse), (float)(p.Velocity.Y * inverse),
                (float)(p.Velocity.Z * inverse));
        }
        var speed = (random() * (double)d.RandomScale + 1) * d.EjectionSpeed;
        p.Velocity = new((float)(p.Velocity.X * speed), (float)(p.Velocity.Y * speed),
            (float)(p.Velocity.Z * speed));
        p.Energy = (float)((double)p.Energy - dt);
    }
}
