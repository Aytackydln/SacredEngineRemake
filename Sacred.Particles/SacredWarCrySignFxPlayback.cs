using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native strict phase boundaries and the renderer's single-wrap sweeping fins.</summary>
public sealed class SacredWarCrySignFxPlayback
{
    private SacredWarCrySignFxStateLayout _state;
    public SacredWarCrySignFxPlayback(SacredWarCrySignFxDefinition definition) { Definition = definition; Restart(); }
    public SacredWarCrySignFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public byte Phase => _state.Phase;
    public SacredWarCrySignFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredWarCrySignFxStateLayout state) => _state = state;
    public void Restart() => _state = new()
    {
        Active = 1, Lifetime = Definition.InitialLifetime,
        MinimumReach = Definition.MinimumReach, MaximumReach = Definition.MaximumReach
    };
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)elapsed;
        var sweep = _state.Sweep - seconds * (double)Definition.SweepRate;
        _state.Sweep = (float)sweep;
        if (sweep < 0) _state.Sweep = (float)(sweep + Definition.SweepPeriod);
        if (_state.Phase == 0)
        {
            var weight = _state.Weight + seconds * (double)Definition.WeightRate;
            _state.Weight = (float)weight;
            if (weight > 1) { _state.Weight = 1; _state.Phase = 1; }
        }
        else if (_state.Phase == 1)
        {
            if (elapsed > _state.Lifetime) _state.Phase = 2;
        }
        else if (_state.Phase == 2)
        {
            var weight = _state.Weight - seconds * (double)Definition.WeightRate;
            _state.Weight = (float)weight;
            if (weight < 0) { _state.Weight = 0; _state.Active = 0; }
        }
    }
    public SacredSpriteFlareSurface GroundSurface => new(Definition.GroundFlare with
        { HalfSize = (float)(_state.Weight * (double)Definition.GroundFlare.HalfSize) }, false);

    /// <summary>One complete native renderer, retaining both opposite fins and all overdraw.</summary>
    public IReadOnlyList<SacredWarCrySignFxStrip> Strips()
    {
        var d = Definition;
        var result = new List<SacredWarCrySignFxStrip>();
        for (var sector = 0; sector < d.SectorCount; sector++)
        {
            var testedPosition = sector * (double)d.SectorStep;
            var position = (float)testedPosition;
            var end = _state.Sweep + (double)d.SweepWidth;
            // The first test retains x87's product after storing position; the wrap test reloads the float.
            if (!(testedPosition > _state.Sweep && end > position) &&
                !(position + (double)d.SweepPeriod > _state.Sweep && position + (double)d.SweepPeriod < end)) continue;
            var relative = position - (double)_state.Sweep;
            if (relative < 0) relative += d.SweepPeriod;
            relative *= d.ReachFalloff;
            var reach = (float)((1 - relative) * (_state.MaximumReach - (double)_state.MinimumReach) * _state.Weight + _state.MinimumReach);
            var wave = (float)Math.Sin((position + (double)_state.Sweep) * d.FullAngle);
            var angle = (float)(position * (double)d.FullAngle);
            var highlight = wave > d.HighlightThreshold;
            var alpha = unchecked((uint)(int)((wave - (double)d.HighlightThreshold) * d.HighlightAlphaScale));
            var color = alpha << 24 | (d.StripColor & 0xFFFFFF);
            foreach (var rotation in new[] { angle, (float)(angle + (double)d.OppositeAngle) })
            {
                result.Add(new(rotation, reach, d.StripColor));
                if (highlight)
                    for (var pass = 0; pass < d.HighlightPassCount; pass++) result.Add(new(rotation, reach, color));
            }
        }
        return result;
    }
}
