using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native explicit-Euler update order, with scalar births/tails and SIMD live batches.</summary>
public static class ParticleCpuSimulation
{
    // Gold 0x7641E6 / 0x890D70 compares length, not length squared.
    private const float MinimumDirectionLength = 0.0000001f;

    public static void Advance(ref ParticleSimulationState particle, SacredParticleMotionLayout motion,
        float dt, Vector3 direction, bool applyInwardAcceleration = true,
        ParticleGroundCollision collision = ParticleGroundCollision.None, float groundHeight = 0)
    {
        particle.Position += particle.Velocity * dt;
        var acceleration = particle.Gravity * dt;
        particle.Velocity.Z -= acceleration;
        particle.Velocity += direction * acceleration;
        if (applyInwardAcceleration && collision == ParticleGroundCollision.None && motion.InwardAcceleration != 0)
        {
            var length = particle.Position.Length();
            if (length >= MinimumDirectionLength)
                particle.Velocity -= particle.Position * (1 / length) * (motion.InwardAcceleration * dt);
        }
        particle.Rotation += particle.AngularVelocity * dt;
        if (collision != ParticleGroundCollision.None && particle.Position.Z <= groundHeight)
        {
            // 0x7646AE: reflect the updated velocity without clamping position or damping.
            if (collision == ParticleGroundCollision.Die) particle.Fade = 0;
            else particle.Velocity.Z = -particle.Velocity.Z;
        }
        particle.Fade += motion.FadeChangeRate * dt;
        particle.Size += motion.SizeChangeRate * dt;
        particle.Gravity += motion.GravityChangeRate * dt;
        particle.Rotation += motion.AdditionalAngularVelocity * dt;
    }

    internal static void Update(ParticleSimulationBatch b, float dt, Vector3 direction,
        ParticleSimulationMode mode, ParticleGroundCollision collision, float groundHeight)
    {
        var index = 0;
        if (mode == ParticleSimulationMode.CpuSimd && Vector.IsHardwareAccelerated)
        {
            for (; index <= b.Count - Vector<float>.Count; index += Vector<float>.Count)
                AdvanceVector(b, index, dt, direction, collision, groundHeight);
        }
        for (; index < b.Count; index++)
        {
            var particle = b[index];
            Advance(ref particle, b.Parameters.Motion, dt, direction,
                collision: collision, groundHeight: groundHeight);
            b.X[index] = particle.Position.X; b.Y[index] = particle.Position.Y; b.Z[index] = particle.Position.Z;
            b.Vx[index] = particle.Velocity.X; b.Vy[index] = particle.Velocity.Y; b.Vz[index] = particle.Velocity.Z;
            b.Gravity[index] = particle.Gravity; b.Size[index] = particle.Size;
            b.Fade[index] = particle.Fade; b.Rotation[index] = particle.Rotation;
        }
    }

    private static void AdvanceVector(ParticleSimulationBatch b, int i, float dt, Vector3 direction,
        ParticleGroundCollision collision, float groundHeight)
    {
        var motion = b.Parameters.Motion;
        var time = new Vector<float>(dt);
        var vx = new Vector<float>(b.Vx, i);
        var vy = new Vector<float>(b.Vy, i);
        var vz = new Vector<float>(b.Vz, i);
        var x = new Vector<float>(b.X, i) + vx * time;
        var y = new Vector<float>(b.Y, i) + vy * time;
        var z = new Vector<float>(b.Z, i) + vz * time;
        var gravity = new Vector<float>(b.Gravity, i);
        var acceleration = gravity * time;
        vx += acceleration * new Vector<float>(direction.X);
        vy += acceleration * new Vector<float>(direction.Y);
        vz = (vz - acceleration) + acceleration * new Vector<float>(direction.Z);
        if (collision == ParticleGroundCollision.None && motion.InwardAcceleration != 0)
        {
            var length = Vector.SquareRoot(x * x + y * y + z * z);
            var reciprocal = Vector.ConditionalSelect(
                Vector.GreaterThanOrEqual(length, new Vector<float>(MinimumDirectionLength)),
                Vector<float>.One / Vector.Max(length, new Vector<float>(MinimumDirectionLength)),
                Vector<float>.Zero);
            var force = new Vector<float>(motion.InwardAcceleration * dt);
            vx -= (x * reciprocal) * force;
            vy -= (y * reciprocal) * force;
            vz -= (z * reciprocal) * force;
        }
        var fade = new Vector<float>(b.Fade, i);
        if (collision != ParticleGroundCollision.None)
        {
            var hit = Vector.LessThanOrEqual(z, new Vector<float>(groundHeight));
            if (collision == ParticleGroundCollision.Die)
                fade = Vector.ConditionalSelect(hit, Vector<float>.Zero, fade);
            else vz = Vector.ConditionalSelect(hit, -vz, vz);
        }
        x.CopyTo(b.X, i); y.CopyTo(b.Y, i); z.CopyTo(b.Z, i);
        vx.CopyTo(b.Vx, i); vy.CopyTo(b.Vy, i); vz.CopyTo(b.Vz, i);
        (fade + new Vector<float>(motion.FadeChangeRate * dt)).CopyTo(b.Fade, i);
        (new Vector<float>(b.Size, i) + new Vector<float>(motion.SizeChangeRate * dt)).CopyTo(b.Size, i);
        (gravity + new Vector<float>(motion.GravityChangeRate * dt)).CopyTo(b.Gravity, i);
        ((new Vector<float>(b.Rotation, i) + new Vector<float>(b.AngularVelocity, i) * time) +
         new Vector<float>(motion.AdditionalAngularVelocity * dt)).CopyTo(b.Rotation, i);
    }
}
