using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredSpriteFlareFxPlayback
{
    private SacredBfgFxStateLayout _bfg;
    private SacredTrap02FxStateLayout _trap;
    private ushort _flags;
    private float _groundOffset;
    public SacredSpriteFlareFxPlayback(SacredSpriteFlareFxDefinition definition, float groundFlareHeightOffset = 0)
    { Definition = definition; _groundOffset = groundFlareHeightOffset; Restart(); }
    public SacredSpriteFlareFxDefinition Definition { get; }
    public uint Notification { get; private set; }
    public bool Active => Definition.OscillatesColors ? _bfg.Active != 0 : _trap.Active != 0;
    public SacredSpriteFlareSnapshot CaptureState() => new(_bfg, _trap, _flags, Notification, _groundOffset);
    public void RestoreState(SacredSpriteFlareSnapshot state)
    { _bfg = state.Bfg; _trap = state.Trap; _flags = state.Flags; Notification = state.Notification; _groundOffset = state.GroundFlareHeightOffset; }
    public void Restart()
    { _bfg = new() { Active = 1 }; _trap = new() { Active = 1 }; _flags = 0; Notification = Definition.InitialNotification; }
    public void RequestRemoval()
    {
        if (Definition.RemovalFlag == 0) throw new InvalidOperationException("This native visual has no removal flag.");
        _flags |= Definition.RemovalFlag;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Definition.OscillatesColors)
        {
            _trap.ElapsedTime = (float)(_trap.ElapsedTime + (double)seconds);
            if ((_flags & Definition.RemovalFlag) != 0) { _flags &= (ushort)~Definition.RemovalFlag; _trap.Active = 0; }
            return;
        }
        var elapsed = _bfg.ElapsedTime + (double)seconds;
        _bfg.ElapsedTime = (float)elapsed;
        if (elapsed >= Definition.Lifetime) _bfg.Active = 0;
        else if ((_flags & Definition.RemovalFlag) != 0)
        { _flags &= (ushort)~Definition.RemovalFlag; _bfg.Active = 0; Notification = 0; }
        // The original phase uses the extended elapsed value, before its float store rounds it.
        var phase = elapsed * Definition.ColorFrequency;
        var sine = unchecked((uint)(int)((Math.Sin(phase) + 1) * Definition.ColorAmplitude));
        var cosine = unchecked((uint)(int)((Math.Cos(phase) + 1) * Definition.ColorAmplitude));
        uint Gray(uint intensity) => Definition.ColorBase | intensity << 16 | intensity << 8 | intensity;
        _bfg.SineColor = Gray(sine); _bfg.ComplementColor = Gray(Definition.ComplementIntensity - sine); _bfg.CosineColor = Gray(cosine);
    }
    public float Radius()
    {
        var fraction = _trap.ElapsedTime - Math.Floor(_trap.ElapsedTime);
        // The second branch subtracts one before multiplying: retain the native operation order.
        var pulse = fraction < Definition.PulseThreshold ? fraction * Definition.PulseAmplitude * Definition.PulseRiseScale
            : -(fraction - 1) * Definition.PulseAmplitude * Definition.PulseFallScale;
        return (float)(Definition.Layers[0].HalfSize + pulse);
    }
    public IReadOnlyList<SacredSpriteFlareSurface> Surfaces()
    {
        var result = new List<SacredSpriteFlareSurface>();
        for (var index = 0; index < Definition.Layers.Count; index++)
        {
            var layer = Definition.Layers[index];
            if (Definition.OscillatesColors) layer = layer with { Color = index switch { 0 => _bfg.SineColor, 1 => _bfg.ComplementColor, _ => _bfg.CosineColor } };
            else layer = layer with { HalfSize = Radius() };
            result.Add(new(layer, true));
        }
        if (Definition.GroundFlare is { } ground) result.Add(new(ground with { Offset = ground.Offset + new Vector3(0, 0, -_groundOffset) }, false));
        return result;
    }
}
