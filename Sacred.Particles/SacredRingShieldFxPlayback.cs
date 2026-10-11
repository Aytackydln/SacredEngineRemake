using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Both native shield timers, hit consumption and authored surface transforms.</summary>
public sealed class SacredRingShieldFxPlayback
{
    private SacredHolyShieldFxStateLayout _holy;
    private SacredMagicShieldFxStateLayout _magic;
    public SacredRingShieldFxPlayback(SacredRingShieldFxDefinition definition) { Definition = definition; Restart(); }
    public SacredRingShieldFxDefinition Definition { get; }
    public bool Active => Definition.Holy is not null ? _holy.Active != 0 : _magic.Active != 0;
    public ushort HitFlags { get; set; }
    public uint Notification { get; private set; }
    public int Phase => _magic.Phase;
    public int MarkerCount => (int)_magic.Segments;
    public int RingVertexCount => Definition.Holy is not null ? Definition.RingVertices.Count : (MarkerCount + 1) * 2;
    public void Restart()
    {
        _holy = new() { Active = 1, Lifetime = Definition.InitialLifetime };
        _magic = new() { Active = 1, Lifetime = Definition.InitialLifetime };
        HitFlags = 0; Notification = 0;
    }
    public void RequestHit() => HitFlags |= SacredRingShieldFxHitFlagsLayout.HitMask;
    public SacredHolyShieldFxStateLayout CaptureHolyState() => _holy;
    public SacredMagicShieldFxStateLayout CaptureMagicState() => _magic;
    public void RestoreState(in SacredHolyShieldFxStateLayout state) { _holy = state; HitFlags = 0; Notification = 0; }
    public void RestoreState(in SacredMagicShieldFxStateLayout state) { _magic = state; HitFlags = 0; Notification = 0; }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Definition.Holy is { } holy) AdvanceHoly(seconds, holy);
        else AdvanceMagic(seconds, Definition.Magic!);
        if ((HitFlags & SacredRingShieldFxHitFlagsLayout.HitMask) == 0) return;
        HitFlags &= unchecked((ushort)~SacredRingShieldFxHitFlagsLayout.HitMask);
        if (Definition.Holy is { } h) _holy.HitBrightness = h.HitBrightness;
        else _magic.HitFlash = Definition.Magic!.HitFlash;
    }
    private void AdvanceHoly(float seconds, SacredHolyShieldFxParameters p)
    {
        var elapsed = _holy.ElapsedTime + (double)seconds;
        _holy.ElapsedTime = (float)elapsed;
        if (elapsed > _holy.Lifetime) _holy.Active = 0;
        var phase = _holy.RotationPhase - seconds * (double)p.RotationRate;
        _holy.RotationPhase = (float)(phase < 0 ? phase + p.RotationWrap : phase);
        if (_holy.HitBrightness > 0)
            _holy.HitBrightness = Math.Max(0, _holy.HitBrightness - (int)(seconds * (double)p.HitFadeRate));
    }
    private void AdvanceMagic(float seconds, SacredMagicShieldFxParameters p)
    {
        var elapsed = _magic.ElapsedTime + (double)seconds;
        _magic.ElapsedTime = (float)elapsed;
        switch (_magic.Phase)
        {
            case 0:
                var growing = _magic.Segments + seconds * (double)p.GrowthRate;
                _magic.Segments = (float)growing;
                if (growing > p.MaximumSegments)
                { _magic.Segments = p.MaximumSegments; _magic.Phase = 1; Notification = 6; }
                break;
            case 1:
                if (elapsed > _magic.Lifetime) _magic.Phase = 2;
                if (_magic.HitFlash > 0) _magic.HitFlash = (float)(_magic.HitFlash - seconds * (double)p.HitFadeRate);
                break;
            case 2:
                var shrinking = _magic.Segments - seconds * (double)p.GrowthRate;
                _magic.Segments = (float)shrinking;
                if (shrinking < 0) { _magic.Segments = 0; _magic.Active = 0; }
                break;
        }
    }
    public uint RingColor(int vertex)
    {
        if (Definition.Holy is not { } p) return Definition.RingVertices[vertex].Color;
        var pairs = Definition.RingVertices.Count / 2;
        var start = (int)(_holy.RotationPhase * (double)p.GradientIndexScale);
        var relative = (vertex / 2 - start % pairs + pairs) % pairs;
        var channel = vertex % 2 == 1 ? _holy.HitBrightness
            : Math.Min(255, _holy.HitBrightness + Math.Max(0, p.GradientStart - relative * p.GradientStep));
        return (uint)(channel << 16 | channel << 8 | channel);
    }
    public Matrix4x4 RingMatrix(int pass)
    {
        if (Definition.Magic is not { } p) return Matrix4x4.Identity;
        var wave = pass == 0 ? Math.Cos(_magic.ElapsedTime * (double)p.FirstHeightWaveRate)
            : Math.Sin(_magic.ElapsedTime * (double)p.SecondHeightWaveRate);
        return Matrix4x4.CreateScale(1, 1, (float)(wave * p.HeightScaleWave + _magic.HitFlash + p.HeightScaleOrigin));
    }
    public Matrix4x4 MarkerMatrix(int index)
    {
        var p = Definition.Magic!;
        var scale = (float)(Math.Cos(_magic.ElapsedTime) * p.MarkerScaleWave + p.MarkerScaleOrigin);
        var angle = index * (double)Definition.AngleStep;
        var center = new Vector3((float)(Math.Sin(angle) * Definition.Radius), (float)(Math.Cos(angle) * Definition.Radius), 0);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center);
    }
    public Vector3 GroundVertex(int corner)
    {
        var p = Definition.Holy!;
        var angle = (float)(_holy.RotationPhase * (double)p.GroundAngleScale + p.GroundAngleOrigin - p.GroundAngleBias);
        return Vector3.Transform(new(corner < 2 ? p.GroundHalfSize : -p.GroundHalfSize,
            (corner % 2 * 2 - 1) * p.GroundHalfSize, 0), Matrix4x4.CreateRotationZ(-angle));
    }
}
