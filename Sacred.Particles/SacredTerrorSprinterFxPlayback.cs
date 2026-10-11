using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native phase controller; the constructor randomizes segment resources once, outside the saved state.</summary>
public sealed class SacredTerrorSprinterFxPlayback
{
    private SacredTerrorSprinterFxStateLayout _state;
    private readonly int[] _textures;
    public SacredTerrorSprinterFxPlayback(SacredTerrorSprinterFxDefinition definition, Func<int>? nextRandom=null)
    {
        Definition=definition; uint randomState=1;
        int Next() { randomState=unchecked(randomState*214013u+2531011u); return (int)(randomState>>16 & 0x7FFF); }
        nextRandom ??= Next;
        _textures=Enumerable.Range(0,definition.SegmentCount).Select(_ => nextRandom()%definition.SegmentTextureVariants.Count).ToArray();
        Restart();
    }
    public SacredTerrorSprinterFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public byte Phase => _state.Phase;
    public SacredTerrorSprinterFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredTerrorSprinterFxStateLayout state) => _state=state;
    public string SegmentTexture(int segment) => Definition.SegmentTextureVariants[_textures[segment]];
    public void Restart() => _state=Definition.InitialState;
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed=_state.ElapsedTime+(double)seconds; _state.ElapsedTime=(float)elapsed;
        if (_state.Phase==0)
        {
            var weight=_state.Weight+seconds*(double)Definition.WeightRate; _state.Weight=(float)weight;
            if (weight>1) { _state.Weight=1; _state.Phase=1; }
        }
        else if (_state.Phase==1) { if (elapsed>_state.Lifetime) _state.Phase=2; }
        else if (_state.Phase==2)
        {
            var weight=_state.Weight-seconds*(double)Definition.WeightRate; _state.Weight=(float)weight;
            if (weight<0) { _state.Weight=0; _state.Active=0; }
        }
    }
    public SacredSpriteFlareSurface GroundSurface
    {
        get
        {
            var redBlue=unchecked((uint)(int)(_state.Weight*(double)Definition.RedBlueScale));
            var green=unchecked((uint)(int)(_state.Weight*(double)Definition.GreenScale));
            var color=(((redBlue | 0xFFFFFF00)<<8 | green)<<8) | redBlue;
            return new(Definition.GroundFlare with { Color=color },false);
        }
    }
    public Matrix4x4 GroundMatrix => Matrix4x4.CreateRotationZ(-(float)(_state.ElapsedTime*(double)Definition.GroundFlare.RotationRate));
    public SacredFxRingSurface RingSurface(Vector3 position=default)
    {
        var width=_state.Weight*(double)Definition.RingHalfWidth;
        return new(Definition.Ring,Definition.SegmentCount,(float)(Definition.RingRadius-width),
            (float)(Definition.RingRadius+width),Definition.RingColor,position);
    }
}
