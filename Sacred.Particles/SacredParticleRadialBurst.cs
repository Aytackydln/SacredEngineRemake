using System.Numerics;

namespace Sacred.Particles;

/// <summary>Native full-vector ring burst. The three CRT samples are angle, mass and rotation,
/// in that order; subsequent movement uses the separately recovered motion block.</summary>
public sealed record SacredParticleRadialBurst(float Radius, float RandomScale, float FullAngle,
    float MassMinimum, float MassWidth, float MassMultiplier, float VerticalVelocity,
    float Size, float Energy, uint NativeAddress)
{
    public ParticleSimulationState Create(Func<int> random, int drawOrder)
    {
        var angle = random() * (double)RandomScale * FullAngle;
        var position = new Vector3((float)(Math.Cos(angle) * Radius), (float)(Math.Sin(angle) * Radius), 0);
        var mass = (float)((random() * (double)RandomScale * MassWidth + MassMinimum) * MassMultiplier);
        var rotation = (float)(random() * (double)RandomScale * FullAngle);
        return new() { Position = position, Velocity = new(0, 0, VerticalVelocity), Gravity = mass,
            Size = Size, Fade = Energy, Rotation = rotation, DrawOrder = drawOrder };
    }
}
