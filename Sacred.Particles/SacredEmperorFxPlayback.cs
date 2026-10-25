using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Movement precedes the old-time emission test. Hit processing follows emission.</summary>
public sealed class SacredEmperorFxPlayback
{
    private SacredEmperorFxStateLayout _state;
    private ushort _flags;
    private uint _hitKind;
    private uint _randomState;
    private readonly Func<int> _nextRandom;

    public SacredEmperorFxPlayback(SacredEmperorFxDefinition definition, uint seed = 1, Func<int>? nextRandom = null)
    {
        Definition = definition; _randomState = seed; _nextRandom = nextRandom ?? NextRandom; Restart();
    }
    public SacredEmperorFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public float ElapsedTime => _state.ElapsedTime;
    public bool Emitting => _state.ElapsedTime < _state.EmissionEnd;
    public uint Notification { get; private set; }
    public uint RenderRadius { get; private set; }
    public SacredParticleEmissionLayout Emission => _state.Emission;
    public SacredEmperorFxSnapshot CaptureState() => new(_state, _flags, _hitKind, Notification, RenderRadius, _randomState);
    public void RestoreState(SacredEmperorFxSnapshot snapshot)
    {
        _state = snapshot.State; _flags = snapshot.Flags; _hitKind = snapshot.HitKind;
        Notification = snapshot.Notification; RenderRadius = snapshot.RenderRadius; _randomState = snapshot.RandomState;
    }
    public void Restart()
    {
        var padding = (_state.Unknown005, _state.Unknown006);
        _state = Definition.InitialState; (_state.Unknown005, _state.Unknown006) = padding;
        _flags = 0; _hitKind = 0; Notification = Definition.InitialNotification; RenderRadius = Definition.InitialRadius;
    }
    public void RequestHit(uint kind = 0) { _flags |= Definition.HitFlag; _hitKind = kind; }
    /// <summary>Particle manager 7CBA10 assigns the entire +74 word; it does not OR notifications.</summary>
    public void SetParticleFlags(ushort flags) => _flags = flags;

    /// <returns>The native old-time emission decision, after the caller has moved its particles.</returns>
    public bool Advance(float seconds, bool survivingParticles)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var emitting = Emitting;
        if (!emitting && !survivingParticles) _state.Active = 0;
        var elapsed = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)elapsed;
        RenderRadius = unchecked(RenderRadius + (uint)(int)(seconds * (double)Definition.RadiusRate));
        if ((_flags & Definition.HitFlag) != 0)
        {
            _flags &= (ushort)~Definition.HitFlag;
            Notification = _hitKind == 1 ? Definition.SpecialHitNotification : Definition.HitNotification;
            _state.EmissionEnd = (float)(elapsed + Definition.HitExtension);
            _state.FlareSizeMultiplier = Definition.HitFlareMultiplier;
            _state.Emission = Definition.Resize(_state.Emission);
        }
        return emitting;
    }
    /// <summary>stdFlare consumes exactly one CRT draw only while stored age is below the end.</summary>
    public float? RenderHalfSize() => Emitting
        ? (float)((_nextRandom() * (double)Definition.RandomScale * Definition.FlareSizeWidth + Definition.FlareSizeMinimum) * _state.FlareSizeMultiplier)
        : null;
    private int NextRandom()
    {
        _randomState = unchecked(_randomState * 214013 + 2531011);
        return (int)(_randomState >> 16 & 32767);
    }
}

public sealed record SacredEmperorFxSnapshot(SacredEmperorFxStateLayout State, ushort Flags, uint HitKind,
    uint Notification, uint RenderRadius, uint RandomState);
