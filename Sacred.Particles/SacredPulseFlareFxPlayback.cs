using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredPulseFlareFxPlayback
{
    private SacredTransdimFxStateLayout _transdim;
    private SacredPongBallFxStateLayout _ball;
    private SacredZhadumFxStateLayout _zhadum;
    private ushort _flags;
    private uint _randomState;
    public SacredPulseFlareFxPlayback(SacredPulseFlareFxDefinition definition) { Definition = definition; Restart(); }
    public SacredPulseFlareFxDefinition Definition { get; }
    public uint Notification { get; private set; }
    public uint Speed { get; private set; }
    public bool Active => Definition.Kind switch
    { SacredPulseFlareKind.Transdim => _transdim.Active != 0, SacredPulseFlareKind.PongBall => _ball.Active != 0, _ => _zhadum.Active != 0 };
    public SacredPulseFlareSnapshot CaptureState() => new(_transdim, _ball, _zhadum, _flags, Notification, Speed, _randomState);
    public void RestoreState(SacredPulseFlareSnapshot s)
    { _transdim = s.Transdim; _ball = s.PongBall; _zhadum = s.Zhadum; _flags = s.Flags; Notification = s.Notification; Speed = s.Speed; _randomState = s.RandomState; }
    public void Restart()
    {
        _transdim = new() { Active = 1, Lifetime = Definition.Lifetime };
        _ball = new() { Active = 1, SpeedTimer = Definition.InitialTimer, HitWeight = 1 };
        _zhadum = new() { Active = 1 };
        _flags = 0; _randomState = 1; Notification = Definition.InitialNotification; Speed = Definition.InitialSpeed;
    }
    public void RequestHit()
    {
        if (Definition.HitFlag == 0) throw new InvalidOperationException("This native controller has no hit flag.");
        _flags |= Definition.HitFlag;
    }
    private int NextRandom()
    { _randomState = unchecked(_randomState * 214013u + 2531011u); return (int)(_randomState >> 16 & 0x7FFF); }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var d = Definition;
        switch (d.Kind)
        {
            case SacredPulseFlareKind.Transdim:
                var elapsed = _transdim.ElapsedTime + (double)seconds; _transdim.ElapsedTime = (float)elapsed;
                if (elapsed > _transdim.Lifetime) _transdim.Active = 0;
                var cycle = _transdim.CycleTime + (double)seconds; _transdim.CycleTime = (float)cycle;
                if (cycle > d.ResetInterval)
                {
                    _transdim.CycleTime = 0;
                    _transdim.OffsetY = (float)(NextRandom() * (double)d.RandomScale * d.PositionRange - d.PositionBias);
                    _transdim.OffsetX = (float)(NextRandom() * (double)d.RandomScale * d.PositionRange - d.PositionBias);
                    _transdim.OffsetZ = 0;
                    _transdim.Angle = (float)(NextRandom() * (double)d.RandomScale * d.FullAngle);
                }
                break;
            case SacredPulseFlareKind.PongBall:
                _ball.ElapsedTime = (float)(_ball.ElapsedTime + (double)seconds);
                var timer = _ball.SpeedTimer - (double)seconds; _ball.SpeedTimer = (float)timer;
                if (timer < 0) { _ball.SpeedTimer = d.ResetTimer; Speed = Math.Min(unchecked(Speed + d.SpeedIncrement), d.SpeedLimit); }
                if ((_flags & d.HitFlag) != 0)
                { _flags &= (ushort)~d.HitFlag; Notification = 0; _ball.HitWeight = 1; _ball.SpeedTimer = d.HitTimer; Speed = d.InitialSpeed; }
                if (Notification == 0)
                {
                    var weight = _ball.HitWeight - seconds * (double)d.FadeRate; _ball.HitWeight = (float)weight;
                    if (weight < 0) { _ball.HitWeight = 0; Notification = d.ResumeNotification; }
                }
                break;
            case SacredPulseFlareKind.Zhadum:
                _zhadum.ElapsedTime = (float)(_zhadum.ElapsedTime + (double)seconds);
                if ((_flags & d.HitFlag) != 0) { _flags &= (ushort)~d.HitFlag; _zhadum.HoldTime = d.InitialTimer; }
                if (_zhadum.HoldTime > 0)
                {
                    var hold = _zhadum.HoldTime - (double)seconds; _zhadum.HoldTime = (float)hold;
                    if (hold < 0) { _zhadum.HoldTime = 0; _zhadum.Brightness = 1; }
                }
                if (_zhadum.Brightness > 0)
                {
                    var brightness = _zhadum.Brightness - seconds * (double)d.FadeRate;
                    _zhadum.Brightness = brightness < 0 ? 0 : (float)brightness;
                }
                break;
        }
    }
    /// <summary>One complete native render; Transdim consumes fifteen CRT samples per call.</summary>
    public IReadOnlyList<SacredPulseFlareSurface> Surfaces()
    {
        var d = Definition; var result = new List<SacredPulseFlareSurface>();
        switch (d.Kind)
        {
            case SacredPulseFlareKind.Transdim:
                var spread = (float)(_transdim.CycleTime * (double)_transdim.CycleTime * d.SpreadScale);
                for (var i = 0; i < d.PlaneCount; i++)
                {
                    var step = i * (double)d.PlaneStep - 1;
                    var alpha = unchecked((uint)(int)((d.AlphaBias - Math.Abs(step)) * d.AlphaScale));
                    if (i == (NextRandom() & 15)) alpha = d.FlashAlpha;
                    var offset = new Vector3((float)(step * spread + _transdim.OffsetX), _transdim.OffsetY, _transdim.OffsetZ);
                    result.Add(new(d.Layers[0] with { Color = alpha << 24 | d.Layers[0].Color, Offset = offset }, false, _transdim.Angle));
                }
                break;
            case SacredPulseFlareKind.PongBall:
                if (Notification == 0)
                    foreach (var layer in d.Layers.Take(2)) result.Add(new(layer with
                    { HalfSize = (float)(_ball.HitWeight * (double)_ball.HitWeight * layer.HalfSize) }, false,
                        (float)(_ball.ElapsedTime * (double)layer.RotationRate)));
                var radius = (float)((Math.Cos(_ball.ElapsedTime * (double)d.RadiusFrequency) * d.RadiusAmplitude + d.RadiusBias) * (1 - (double)_ball.HitWeight));
                result.Add(new(d.Layers[2] with { HalfSize = (float)(radius * (double)d.Layers[2].HalfSize) }, false));
                result.Add(new(d.Layers[3] with { HalfSize = radius }, true));
                break;
            case SacredPulseFlareKind.Zhadum:
                var intensity = unchecked((uint)(int)((_zhadum.HoldTime > 0 ? 1 : (double)_zhadum.Brightness) * d.AlphaScale));
                result.Add(new(d.Layers[0] with { Color = intensity << 16 | intensity << 8 | intensity }, true));
                break;
        }
        return result;
    }
}
