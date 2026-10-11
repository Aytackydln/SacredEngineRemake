using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native scrolling material, glow-line sampling and one-second impact envelope.</summary>
public sealed class SacredPongBatterFxPlayback(SacredPongBatterFxDefinition definition)
{
    private SacredPongBatterFxStateLayout _state = new() { Active = 1 };
    private bool _hitPending;
    public SacredPongBatterFxDefinition Definition { get; } = definition;
    public SacredPongBatterFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredPongBatterFxStateLayout state) { _state = state; _hitPending = false; }
    public void Restart() { _state = new() { Active = 1 }; _hitPending = false; }
    /// <summary>Equivalent to setting base flags +74 bit 8; consumed after the next update's decrement.</summary>
    public void RequestHit() => _hitPending = true;

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime = (float)(_state.ElapsedTime + (double)seconds);
        if (_state.HitFlash > 0) _state.HitFlash = Math.Max(0, (float)(_state.HitFlash - (double)seconds));
        if (_hitPending) { _state.HitFlash = Definition.HitFlashReset; _hitPending = false; }
    }

    public float EdgeHalfSize => (float)(Math.Sin(_state.ElapsedTime) * Definition.EdgeSizeWave +
        _state.HitFlash * (double)Definition.EdgeSizeHit + Definition.EdgeSizeOrigin);
    public float EdgeDensity => (float)(Definition.EdgeDensityOrigin - _state.HitFlash * (double)Definition.EdgeDensityHit);
    public uint SurfaceColor
    {
        get
        {
            var value = (uint)(int)((Math.Sin(_state.ElapsedTime) + _state.HitFlash) * Definition.SurfaceColorWave + Definition.SurfaceColorOrigin);
            return value | value << 8 | value << 16;
        }
    }
    public Vector2 SurfaceTextureCoordinate(Vector2 authored) => authored * Definition.TextureScale +
        new Vector2((float)Math.Sin(_state.ElapsedTime * (double)Definition.UScrollRate),
            (float)Math.Cos(_state.ElapsedTime * (double)Definition.VScrollRate));

    public int EdgeSpriteCount(int edge)
    {
        var segment = Definition.Edges[edge];
        var dx = segment.End.X - (double)segment.Start.X;
        var dy = (float)(segment.End.Y - (double)segment.Start.Y);
        var dz = segment.End.Z - (double)segment.Start.Z;
        return Math.Max(1, (int)(Math.Sqrt(dx * dx + dy * (double)dy + dz * dz) * EdgeDensity));
    }

    public Vector3 EdgeSpritePosition(int edge, int sprite)
    {
        var segment = Definition.Edges[edge];
        var count = EdgeSpriteCount(edge);
        if ((uint)sprite >= count) throw new ArgumentOutOfRangeException(nameof(sprite));
        var inverse = 1d / count;
        var step = new Vector3((float)((segment.End.X - (double)segment.Start.X) * inverse),
            (float)((float)(segment.End.Y - (double)segment.Start.Y) * inverse),
            (float)((segment.End.Z - (double)segment.Start.Z) * inverse));
        return new((float)(segment.Start.X + sprite * (double)step.X),
            (float)(segment.Start.Y + sprite * (double)step.Y), (float)(segment.Start.Z + sprite * (double)step.Z));
    }
}
