using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Count-scaled ring births precede the mine-use flag. Retirement follows
/// the final decrement, independently of surviving particles.</summary>
public sealed class SacredMineSignFxPlayback
{
    private SacredMineSignFxStateLayout _state;
    private ushort _flags;
    private uint _randomState;
    private readonly Func<int> _nextRandom;

    public SacredMineSignFxPlayback(SacredMineSignFxDefinition definition, uint seed = 1, Func<int>? nextRandom = null)
    {
        Definition = definition; _randomState = seed; _nextRandom = nextRandom ?? NextRandom; Restart();
    }
    public SacredMineSignFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public float ElapsedTime => _state.ElapsedTime;
    public uint RemainingMines => _state.RemainingMines;
    public SacredParticleEmissionLayout Emission => _state.Emission;
    public SacredMineSignFxSnapshot CaptureState() => new(_state, _flags, _randomState);
    public void RestoreState(SacredMineSignFxSnapshot snapshot)
    {
        _state = snapshot.State; _flags = snapshot.Flags; _randomState = snapshot.RandomState;
    }
    public void Restart()
    {
        var padding = (_state.Unknown005, _state.Unknown006);
        _state = Definition.InitialState; (_state.Unknown005, _state.Unknown006) = padding; _flags = 0;
    }
    public void RequestMineUsed() => _flags |= Definition.MineUsedFlag;

    /// <summary>The caller moves existing particles first. The callback requests one
    /// standard birth even when the particle vector is full. Audio consumes a CRT draw.</summary>
    public int Advance(float seconds, Action<SacredParticleEmissionLayout>? birth = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime = (float)(_state.ElapsedTime + (double)seconds);
        // Native converts the numerator as uint but divides by a signed dword.
        var carry = _state.RemainingMines * (double)seconds * Definition.BirthRate /
            unchecked((int)_state.InitialMines) + _state.BirthCarry;
        _state.BirthCarry = carry > Definition.MaximumCarry ? 0 : (float)carry;
        var births = 0;
        while (_state.BirthCarry >= Definition.BirthUnit)
        {
            var angle = (float)(_nextRandom() * (double)Definition.RandomScale *
                (_state.RemainingMines * (double)Definition.FullAngle / unchecked((int)_state.InitialMines)));
            // Native stores angle over the stack argument that previously held dt.
            var rotation = (float)(_nextRandom() * (double)Definition.RandomScale * Definition.RotationWidth -
                Definition.RotationHalfWidth - angle + Definition.RotationBase);
            _state.Emission = Definition.WithBirth(_state.Emission,
                (float)(Definition.RingRadius * Math.Cos(angle)), (float)(Definition.RingRadius * Math.Sin(angle)), rotation);
            birth?.Invoke(_state.Emission); births++;
            _state.BirthCarry = (float)((double)_state.BirthCarry - Definition.BirthUnit);
        }
        if ((_flags & Definition.MineUsedFlag) != 0)
        {
            _flags &= (ushort)~Definition.MineUsedFlag;
            _state.RemainingMines = unchecked(_state.RemainingMines - 1);
            _nextRandom(); // Native mine-use audio pitch; playback has no audio service.
            if (_state.RemainingMines == 0) _state.Active = 0;
        }
        return births;
    }
    private int NextRandom()
    {
        _randomState = unchecked(_randomState * 214013 + 2531011);
        return (int)(_randomState >> 16 & 32767);
    }
}

public sealed record SacredMineSignFxSnapshot(SacredMineSignFxStateLayout State, ushort Flags, uint RandomState);
