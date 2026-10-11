using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredExpandingBurstFxPlayback
{
    private SacredExpandingBurstFxStateLayout _state;
    public SacredExpandingBurstFxPlayback(SacredExpandingBurstFxDefinition definition) { Definition = definition; Restart(); }
    public SacredExpandingBurstFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public uint RenderRadius { get; private set; }
    public bool RingVisible => _state.ElapsedTime < Definition.RingDuration;
    public bool GroundVisible => _state.ElapsedTime * (double)Definition.GroundTimeRate < 1;
    public SacredExpandingBurstFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredExpandingBurstFxStateLayout state, uint renderRadius = 0) { _state = state; RenderRadius = renderRadius; }
    public void Restart() { _state = Definition.InitialState; RenderRadius = 0; }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime = (float)(_state.ElapsedTime + (double)seconds);
        if (_state.ElapsedTime > Definition.Lifetime) _state.Active = 0;
    }
    /// <summary>Kamikaze changes base +0x98 on every render, even when no ring is submitted.</summary>
    public void BeginRender()
    {
        if (Definition.EventRadiusRate is not null)
            RenderRadius = RingVisible ? unchecked((uint)(long)Radius) : unchecked((uint)(long)(RenderRadius * Definition.RenderRadiusDecay));
    }
    private float Radius => (float)(_state.ElapsedTime * (Definition.EventRadiusRate is { } rate ? (double)rate : Definition.RadiusRate));
    public SacredFxRingSurface RingSurface(Vector3 position = default, float groundHeightOffset = 0)
    {
        var weight = _state.ElapsedTime < Definition.RingFadeStart ? 1 : 1 - (_state.ElapsedTime - (double)Definition.RingFadeStart) * Definition.RingFadeRate;
        uint Channel(float scale) => unchecked((uint)(int)(weight * scale));
        var color = ((Channel(Definition.RingColorScale.X) << 8 | Channel(Definition.RingColorScale.Y)) << 8) | Channel(Definition.RingColorScale.Z);
        return new(Definition.Ring, Definition.SegmentCount, Radius, (float)(Radius + (double)Definition.RingWidth), color,
            new(position.X, position.Y, (float)(position.Z - (double)groundHeightOffset)));
    }
    public SacredSpriteFlareSurface GroundSurface(int layer, Vector3 position = default)
    {
        if ((uint)layer > 1) throw new ArgumentOutOfRangeException(nameof(layer));
        var time = _state.ElapsedTime * (double)Definition.GroundTimeRate;
        var factor = (float)(1 - time * time);
        return new(new(Definition.GroundTexture, (float)(factor * (double)(layer == 0 ? Definition.GroundSizes.X : Definition.GroundSizes.Y)),
            Definition.GroundColor, position, 0, true, true), false);
    }
    public Matrix4x4 GroundMatrix(int layer, Vector3 position = default)
    {
        var angle = (float)(_state.ElapsedTime * (double)(layer == 0 ? Definition.GroundRotations.X : Definition.GroundRotations.Y));
        var matrix = Matrix4x4.CreateRotationZ(-angle);
        matrix.Translation = position;
        return matrix;
    }
}
