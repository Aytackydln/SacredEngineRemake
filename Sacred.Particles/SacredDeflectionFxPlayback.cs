using System.Numerics;

namespace Sacred.Particles;

public static class SacredDeflectionFxPlayback
{
    /// <summary>Orient a combat burst around a fresh random launch axis, preserving the decoded
    /// local cone, trail spacing and speed. Presentation orientation is separate from the native initializer.</summary>
    public static (Vector3[] Positions, Vector3[] Velocities) InitializeBurst(SacredDeflectionFxDefinition definition,
        Func<int> nextRandom)
    {
        var (positions, velocities) = Initialize(definition, nextRandom);
        var height = Math.Clamp(nextRandom() * definition.RandomScale * 2 - 1, -1, 1);
        var angle = nextRandom() * definition.RandomScale * definition.AngularRange;
        var radius = MathF.Sqrt(MathF.Max(0, 1 - height * height));
        var axis = new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), height);
        var cross = Vector3.Cross(Vector3.UnitZ, axis);
        var rotation = cross.LengthSquared() > .0000001f
            ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), MathF.Acos(height))
            : height < 0 ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI) : Quaternion.Identity;
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = Vector3.Transform(positions[i], rotation);
            velocities[i] = Vector3.Transform(velocities[i], rotation);
        }
        return (positions, velocities);
    }

    public static (Vector3[] Positions, Vector3[] Velocities) Initialize(SacredDeflectionFxDefinition definition,
        Func<int> nextRandom)
    {
        double Sample() => nextRandom() * (double)definition.RandomScale;
        var angle = Sample() * definition.AngularRange;
        var axis = Vector3.Normalize(new((float)Math.Cos(angle), (float)Math.Sin(angle), definition.VerticalDirection));
        var positions = new Vector3[definition.StreakCount * definition.TrailPoints];
        var velocities = new Vector3[positions.Length];
        for (var i = 0; i < definition.StreakCount; i++)
        {
            float Spread() => (float)(Sample() * definition.DirectionSpread - definition.DirectionBias);
            var direction = Vector3.Normalize(axis + new Vector3(Spread(), Spread(), Spread()));
            for (var j = 0; j < definition.TrailPoints; j++)
            {
                positions[i * definition.TrailPoints + j] = direction * ((definition.TrailPoints - 1 - j) * definition.TrailSpacing);
                velocities[i * definition.TrailPoints + j] = direction * definition.Speed;
            }
        }
        return (positions, velocities);
    }

    /// <summary>Gold 76C569–76C5E9: integrate position, then damp velocity and subtract life.</summary>
    public static (Vector3 Position, Vector3 Velocity, float Life) AdvancePoint(Vector3 position, Vector3 velocity,
        float life, float seconds, SacredDeflectionFxDefinition definition)
    {
        if (life <= 0) return (position, velocity, life);
        return (position + velocity * seconds, velocity - velocity * (seconds * definition.Damping),
            life - seconds * definition.LifeLoss);
    }
}
