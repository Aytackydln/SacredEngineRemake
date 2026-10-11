using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredBubbleTrapExplosionFxPlayback
{
    private SacredBubbleTrapExplosionFxStateLayout _state;
    public SacredBubbleTrapExplosionFxPlayback(SacredBubbleTrapExplosionFxDefinition definition) { Definition=definition; Restart(); }
    public SacredBubbleTrapExplosionFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public SacredBubbleTrapExplosionFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredBubbleTrapExplosionFxStateLayout state) => _state=state;
    public void Restart() => _state=Definition.InitialState;
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds; _state.ElapsedTime=(float)elapsed;
        _state.InnerRadius=(float)(_state.InnerRadius + seconds*(double)Definition.RadiusRate);
        if (elapsed > Definition.Lifetime) _state.Active=0;
    }
    public SacredFxRingSurface Surface(Vector3 position=default, float groundHeightOffset=0)
    {
        var weight=(Definition.Lifetime-(double)_state.ElapsedTime)*Definition.WeightRate;
        var redBlue=unchecked((uint)(int)(weight*Definition.RedBlueScale));
        var green=unchecked((uint)(int)(weight*Definition.GreenScale));
        var color=((redBlue<<8 | green)<<8) | redBlue;
        return new(Definition.Ring,Definition.SegmentCount,_state.InnerRadius,
            (float)(_state.InnerRadius+(double)Definition.RingWidth),color,
            new(position.X,position.Y,(float)(position.Z-(double)groundHeightOffset)));
    }
}
