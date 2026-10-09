using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native teleport visual state machine. Standalone playback has no owner;
/// teleporting an actor is an independent game action, not a particle operation.</summary>
public sealed class TeleportParticlePlayback
{
    private readonly SacredTeleportParticleDefinition _definition;
    private readonly Func<int> _nextRandom;
    private uint _randomState;
    private SacredTeleportParticleStateLayout _state;
    private readonly SacredTeleportParticleLayout[] _particles;

    public TeleportParticlePlayback(SacredTeleportParticleDefinition definition, int capacity,
        uint seed, Func<int>? nextRandom = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _definition = definition;
        _state = definition.InitialState;
        _particles = new SacredTeleportParticleLayout[capacity];
        _randomState = seed;
        _nextRandom = nextRandom ?? NextRandom;
        // start() writes energy one; initialization happens on the first accepted update.
        foreach (ref var particle in _particles.AsSpan()) particle.Energy = 1;
    }

    public SacredTeleportParticleStateLayout State => _state;
    public ReadOnlySpan<SacredTeleportParticleLayout> Particles => _particles;
    public bool IsAlive => _state.Stage != 6;

    public void Advance(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        if (dt > _definition.MaximumStep) return;
        _state.ElapsedTime = (float)((double)_state.ElapsedTime + dt);
        var scaled = (float)((double)dt * _state.CastSpeed);
        if (_state.ParticleCount == 0)
        {
            foreach (ref var particle in _particles.AsSpan())
                TeleportParticleMovement.Initialize(ref particle, _definition, _nextRandom);
            _state.ParticleCount = (uint)_particles.Length;
        }
        foreach (ref var particle in _particles.AsSpan())
            TeleportParticleMovement.Advance(ref particle, _state, _definition, scaled, _nextRandom);
        AdvanceStage(scaled);
    }

    private void AdvanceStage(float dt)
    {
        var d = _definition;
        switch (_state.Stage)
        {
            case 0:
                if (_state.EnvelopeAngle < d.FullAngle)
                    _state.EnvelopeAngle = (float)(_state.EnvelopeAngle + (double)dt * d.AngleRate);
                else _state.Stage = 1;
                break;
            case 1:
                if (_state.EnvelopeHeight < d.BaseHeight)
                    _state.EnvelopeHeight = (float)(_state.EnvelopeHeight + (double)dt * d.HeightRate);
                else _state.Stage = 2;
                break;
            case 2:
            case 3:
                var wait = (double)_state.WaitToJump - dt;
                _state.WaitToJump = (float)wait;
                if (wait <= 0)
                {
                    _state.WaitToJump = _state.Stage == 2 ? d.InitialState.WaitToJump : d.ReturnWait;
                    _state.Stage++;
                }
                break;
            case 4:
                if (_state.EnvelopeHeight > 0)
                    _state.EnvelopeHeight = (float)(_state.EnvelopeHeight - (double)dt * d.HeightRate);
                else _state.Stage = 5;
                break;
            case 5:
                if (_state.EnvelopeAngle > 0)
                    _state.EnvelopeAngle = (float)(_state.EnvelopeAngle - (double)dt * d.AngleRate);
                else _state.Stage = 6;
                break;
        }
    }

    private int NextRandom()
    {
        _randomState = unchecked(_randomState * 214013u + 2531011u);
        return (int)((_randomState >> 16) & 32767);
    }
}
