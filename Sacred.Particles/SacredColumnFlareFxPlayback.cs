using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredColumnFlareFxPlayback
{
    private SacredColumnFlareFxStateLayout _state;
    public SacredColumnFlareFxPlayback(SacredColumnFlareFxDefinition definition) { Definition = definition; Restart(); }
    public SacredColumnFlareFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public byte Phase => _state.Phase;
    public SacredColumnFlareFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredColumnFlareFxStateLayout state) => _state = state;
    public void Restart() => _state = new() { Active = 1, Lifetime = Definition.InitialLifetime };
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds; _state.ElapsedTime = (float)elapsed;
        switch (_state.Phase)
        {
            case 0:
                var rising = _state.Opacity + seconds * (double)Definition.RiseRate;
                _state.Opacity = (float)rising;
                if (rising > 1) { _state.Opacity = 1; _state.Phase = 1; }
                break;
            case 1:
                if (elapsed > _state.Lifetime - (double)Definition.FadeLeadTime) _state.Phase = 2;
                break;
            case 2:
                var falling = _state.Opacity - seconds * (double)Definition.FallRate;
                _state.Opacity = (float)falling;
                if (falling < 0) { _state.Opacity = 0; _state.Active = 0; }
                break;
        }
    }
    private (double First, float Second) ColorChannels()
    {
        var d = Definition; var cosine = Math.Cos(_state.ElapsedTime * (double)d.ColorFrequency);
        return ((cosine + d.ColorBias) * d.ColorAmplitude + d.ColorOffset,
            (float)((d.ColorBias - cosine) * d.ColorAmplitude + d.ColorOffset));
    }
    public Vector3 LightColor
    {
        get { var channels = ColorChannels(); return new((float)channels.First, channels.Second, Definition.LightBlue); }
    }
    public IReadOnlyList<SacredControlledFlareSurface> Surfaces()
    {
        var d = Definition; var channels = ColorChannels();
        var rgb = unchecked((uint)(int)(channels.First * d.ColorScale) << 16 |
            (uint)(int)(channels.Second * (double)d.ColorScale) << 8) | d.BlueChannel;
        uint WithAlpha(double alpha) => unchecked((uint)(int)alpha << 24) | rgb;
        var angle = (float)(_state.ElapsedTime * (double)d.Layer.RotationRate);
        var spacing = (float)((Math.Sin(_state.ElapsedTime) + d.HeightSineBias) * d.HeightScale * _state.Opacity);
        var surfaces = new List<SacredControlledFlareSurface>(d.SliceCount + 1)
        { new(d.Layer with { Color = WithAlpha(_state.Opacity * (double)d.GroundAlphaScale) }, angle) };
        for (var index = 0; index < d.SliceCount; index++)
            surfaces.Add(new(d.Layer with
            {
                Color = WithAlpha((1 - index * (double)d.SliceAlphaFalloff) * _state.Opacity * d.SliceAlphaScale),
                Offset = d.Layer.Offset + new Vector3(0, 0, (float)(index * (double)spacing))
            }, angle));
        return surfaces;
    }
}
