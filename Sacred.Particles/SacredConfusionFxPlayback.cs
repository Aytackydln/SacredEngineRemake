using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native phase/random schedule, sine alpha, exponential ground height and crossed ribbons.</summary>
public sealed class SacredConfusionFxPlayback
{
    private SacredConfusionFxStateLayout _state;
    public SacredConfusionFxPlayback(SacredConfusionFxDefinition definition) { Definition = definition; Restart(); }
    public SacredConfusionFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public SacredConfusionFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredConfusionFxStateLayout state) => _state = state;
    public void Restart() => _state = Definition.InitialState;
    public void Advance(float seconds, Func<int> nextRandom)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)elapsed;
        if (elapsed > _state.Lifetime) _state.Active = 0;
        var phase = _state.RibbonPhase + seconds * (double)Definition.PhaseRate;
        _state.RibbonPhase = (float)phase;
        if (phase < Definition.PhaseWrap)
        {
            var alpha = (int)(Math.Sin(phase * Definition.AlphaAngleScale) * Definition.AlphaScale);
            _state.Ribbon.Color = _state.Ribbon.Color & 0xFFFFFF | unchecked((uint)alpha << 24);
        }
        else
        {
            _state.RibbonPhase = 0;
            _state.Ribbon.InitialAngle = (float)(nextRandom() * (double)Definition.RandomScale * Definition.RandomAngleScale);
        }
        _state.GroundHeight = (float)(_state.GroundHeight + _state.GroundHeight * (double)seconds);
        if (_state.GroundHeight > Definition.MaximumGroundHeight) _state.GroundHeight = Definition.ResetGroundHeight;
    }
    public Vector3 GroundVertex(int layerIndex, int corner)
    {
        var layer = Definition.GroundLayers[layerIndex];
        var angle = (float)(_state.ElapsedTime * (double)layer.RotationRate);
        var position = Vector3.Transform(new Vector3(corner < 2 ? layer.HalfSize : -layer.HalfSize,
            (corner % 2 * 2 - 1) * layer.HalfSize, 0), Matrix4x4.CreateRotationZ(-angle));
        return position + (layerIndex == 0 ? layer.Offset : new Vector3(_state.GroundOffsetX, _state.GroundOffsetY, _state.GroundHeight));
    }
    public SacredFxSurfaceVertexLayout RibbonVertex(int strip, int vertex)
    {
        var count = Definition.RibbonVertices.Count / 2;
        var source = Definition.RibbonVertices[strip * count + vertex];
        var p = _state.Ribbon;
        var time = (float)(_state.RibbonPhase * (double)Definition.RibbonTimeScale);
        var angle = time * (double)p.AngleStep + p.InitialAngle + vertex / 2 * (double)p.AngleStep;
        var height = time * (double)p.HeightStep + p.InitialHeight + vertex / 2 * (double)p.HeightStep;
        var radius = strip == 0 ? p.Radius + (vertex % 2 == 0 ? -p.HalfWidth : p.HalfWidth) : p.Radius;
        // The original helper stores sin(angle) to float before multiplying the radius.
        var position = new Vector3((float)(Math.Cos(angle) * radius), (float)((float)Math.Sin(angle) * (double)radius),
            (float)(height + (strip == 0 ? 0 : vertex % 2 == 0 ? p.HalfWidth : -p.HalfWidth)));
        return source.WithPosition(position + p.Offset).WithColor(p.Color);
    }
}
