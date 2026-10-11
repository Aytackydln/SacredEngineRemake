using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native jet fade precedes movement and births. Zero strength remains active;
/// strictly negative strength retires the object, even with surviving particles.</summary>
public sealed class SacredBloodFountainFxPlayback
{
    private SacredBloodFountainFxStateLayout _state;
    public SacredBloodFountainFxPlayback(SacredBloodFountainFxDefinition definition)
    { Definition = definition; Restart(); }
    public SacredBloodFountainFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public float ElapsedTime => _state.ElapsedTime;
    public SacredParticleEmissionLayout Emission => _state.Emission;
    public SacredBloodFountainFxStateLayout CaptureState() => _state;
    public void RestoreState(SacredBloodFountainFxStateLayout state) => _state = state;
    public void Restart()
    {
        var padding = (_state.Unknown009, _state.Unknown00A);
        _state = Definition.InitialState;
        (_state.Unknown009, _state.Unknown00A) = padding;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var time = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)time;
        // Native FST retains extended time for this comparison.
        if (time > Definition.FadeStart)
        {
            var strength = _state.JetStrength - seconds * (double)Definition.FadeRate;
            _state.JetStrength = (float)strength;
            if (strength < 0) { _state.JetStrength = 0; _state.Active = 0; }
        }
        _state.Emission = Definition.WithVelocity(_state.Emission,
            (float)(_state.JetStrength * (double)Definition.JetSpeed));
    }
}
