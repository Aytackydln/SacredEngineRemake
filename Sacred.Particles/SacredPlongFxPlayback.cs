using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Original manual burst: seven independent CRT samples per birth, then movement and gravity.</summary>
public sealed class SacredPlongFxPlayback
{
    private SacredPlongFxStateLayout _state;
    private readonly SacredPlongParticleLayout[] _particles;
    private readonly Func<int> _nextRandom;
    private uint _randomState;

    public SacredPlongFxPlayback(SacredPlongFxDefinition definition, int capacity, uint seed = 1, Func<int>? nextRandom = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _state = definition.InitialState;
        _particles = new SacredPlongParticleLayout[capacity]; _randomState = seed;
        _nextRandom = nextRandom ?? NextRandom;
        Restart();
    }
    public SacredPlongFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public ReadOnlySpan<SacredPlongParticleLayout> Particles => _particles;
    public SacredPlongFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredPlongFxStateLayout state, ReadOnlySpan<SacredPlongParticleLayout> particles)
    {
        if (particles.Length != _particles.Length || state.ParticleCount > _particles.Length)
            throw new ArgumentException("Saved particle vector does not match the allocated slots.", nameof(particles));
        _state = state; particles.CopyTo(_particles);
    }
    public void Restart()
    {
        _state.ParticleCount = 0; _state.ElapsedTime = 0; _state.Active = 1;
        _state.CornerColor0 = Definition.InitialState.CornerColor0; _state.CornerColor1 = Definition.InitialState.CornerColor1;
        _state.CornerColor2 = Definition.InitialState.CornerColor2; _state.CornerColor3 = Definition.InitialState.CornerColor3;
        foreach (ref var p in _particles.AsSpan()) p.Energy = 1;
    }
    public void Advance(float seconds, Vector3 position = default)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime = (float)(_state.ElapsedTime + (double)seconds);
        if (_state.ParticleCount == 0)
        {
            foreach (ref var p in _particles.AsSpan()) Initialize(ref p, position);
            _state.ParticleCount = (uint)_particles.Length;
        }
        _state.Active = 0;
        foreach (ref var p in _particles.AsSpan(0, (int)_state.ParticleCount))
        {
            if (!(p.HalfSize > 0)) continue;
            _state.Active = 1; // Tested before shrinking, even on the final visible update.
            p.Position.X = (float)(p.Position.X + seconds * (double)p.Velocity.X);
            p.Position.Y = (float)(p.Position.Y + (double)(float)(seconds * (double)p.Velocity.Y));
            var z = p.Position.Z + (double)(float)(seconds * (double)p.Velocity.Z);
            p.Position.Z = (float)z;
            p.Velocity.Z = (float)(p.Velocity.Z - seconds * (double)Definition.Gravity);
            if (z < 0 && p.Velocity.Z < 0) p.Velocity.Z = (float)(p.Velocity.Z * (double)Definition.BounceFactor);
            p.HalfSize = (float)(p.HalfSize - seconds * (double)Definition.ShrinkRate);
        }
    }
    private void Initialize(ref SacredPlongParticleLayout p, Vector3 position)
    {
        double Sample() => _nextRandom() * (double)Definition.RandomScale;
        p.Position.X = (float)(Sample() * Definition.SpawnWidth - Definition.SpawnOrigin + position.X);
        p.Position.Y = (float)(Sample() * Definition.SpawnWidth - Definition.SpawnOrigin + position.Y);
        p.Position.Z = (float)(Sample() * Definition.SpawnHeight + position.Z);
        p.Velocity.X = (float)(Math.Sin(Sample() * Definition.VelocityAngle) * Definition.HorizontalSpeed);
        p.Velocity.Y = (float)(Math.Cos(Sample() * Definition.VelocityAngle) * Definition.HorizontalSpeed);
        p.Velocity.Z = (float)(Sample() * Definition.VerticalSpeedWidth - Definition.VerticalSpeedOrigin);
        p.HalfSize = Definition.InitialHalfSize;
        p.Rotation = (float)(Sample() * Definition.FullTurn);
    }
    private int NextRandom()
    {
        _randomState = unchecked(_randomState * 214013u + 2531011u);
        return (int)((_randomState >> 16) & 32767);
    }
}
