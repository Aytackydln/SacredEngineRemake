using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native grow/hold/compress/fade controller and direct mesh geometry.</summary>
public sealed class SacredForgeElementFxPlayback(SacredForgeElementFxDefinition definition)
{
    private SacredForgeElementFxStateLayout _state = definition.InitialState;
    private bool _releaseRequested;
    public SacredForgeElementFxDefinition Definition { get; } = definition;
    public SacredForgeElementFxStateLayout State => _state;
    public bool Visible => _state.Phase < 4;
    /// <summary>Native finished air branch requests this type through base +84/+80 after ChildLimit.</summary>
    public uint ChildTypeId { get; private set; }
    /// <summary>Native environment message 7 supplies this frame during the finished air branch.</summary>
    public int? ChildFrame { get; private set; }

    public void Restart() { _state = Definition.InitialState; _releaseRequested = false; ChildTypeId = 0; ChildFrame = null; }
    public void RequestRelease() => _releaseRequested = true;

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime += seconds;
        ChildFrame = null;
        switch (_state.Phase)
        {
            case 0:
                var growing = _state.GrowthFactor + seconds * (double)Definition.GrowthRate;
                _state.GrowthFactor = (float)growing;
                if (growing >= Definition.MaximumGrowthFactor) { _state.GrowthFactor = Definition.MaximumGrowthFactor; _state.Phase = 1; }
                break;
            case 2:
                var compressed = _state.CompressionFactor - seconds * (double)Definition.CompressionRate;
                _state.CompressionFactor = (float)compressed;
                if (compressed <= Definition.MinimumCompressionFactor) { _state.CompressionFactor = Definition.MinimumCompressionFactor; _state.Phase = 3; }
                break;
            case 3:
                var faded = _state.GrowthFactor - seconds * (double)Definition.FadeRate;
                _state.GrowthFactor = (float)faded;
                if (faded <= 0) { _state.GrowthFactor = 0; _state.Phase = 4; }
                break;
            case 4:
                // Original 7A75FB compares the element byte against zero.
                if (_state.Element == 0)
                {
                    var childTime = _state.ChildElapsedTime + (double)seconds;
                    _state.ChildElapsedTime = (float)childTime;
                    if (childTime > Definition.ChildDelay)
                    {
                        ChildFrame = (int)((childTime - Definition.ChildDelay) * Definition.ChildFrameRate);
                        if (_state.ChildElapsedTime > Definition.ChildLimit)
                        {
                            ChildTypeId = Definition.ChildTypeId;
                            _state.Active = 0;
                        }
                    }
                }
                else _state.Active = 0;
                break;
        }
        var scroll = _state.Scroll - seconds * (double)Definition.ScrollRate;
        _state.Scroll = (float)(scroll < 0 ? scroll + Definition.ScrollWrap : scroll);
        // The native request is consumed after this frame's stage, including a zero step.
        if (_releaseRequested) { _state.Phase = 2; _releaseRequested = false; }
    }

    public Vector3 ModelPosition(Vector3 nativeVertex)
    {
        var scale = _state.GrowthFactor * _state.CompressionFactor;
        var angle = _state.ElapsedTime * 2;
        var height = (float)(Math.Sin(_state.ElapsedTime * (double)Definition.ModelBobRate) * Definition.ModelBobAmplitude + Definition.ModelHeight);
        return Rotate(nativeVertex * new Vector3(scale, scale, 1), angle) + new Vector3(0, 0, height);
    }

    public Vector3 RingPosition(int vertex) => Definition.RingVertices[vertex].Position *
        new Vector3(_state.CompressionFactor, _state.CompressionFactor, _state.GrowthFactor) + new Vector3(0, 0, Definition.RingHeight);

    public uint RingColor(int vertex)
    {
        var pairs = Definition.RingVertices.Count / 2;
        var first = (int)(_state.Scroll * (double)pairs);
        var distance = (vertex / 2 - first % pairs + pairs) % pairs;
        var brightness = (uint)Math.Max(0, Definition.TrailStartBrightness - distance * Definition.TrailBrightnessStep);
        return brightness * 0x010101; // Original RGB trail intentionally has zero alpha and source ONE.
    }

    public Vector3 FlarePosition(int pass, int vertex)
    {
        if ((uint)pass > 1 || (uint)vertex > 3) throw new ArgumentOutOfRangeException();
        var scale = pass == 0 ? _state.CompressionFactor : _state.GrowthFactor;
        var size = (float)(scale * (double)Definition.FlareHalfSize);
        var angle = pass == 0 ? (float)(_state.Scroll * (double)Definition.FullTurn + Definition.HalfTurn - Definition.FlareAngleBias) : 0;
        // stdLensflare's final corner writes: right/bottom, right/top, left/bottom, left/top.
        return Rotate(new(vertex < 2 ? size : -size, vertex % 2 == 0 ? -size : size, 0), angle)
            + new Vector3(0, 0, Definition.FlareHeight);
    }

    private static Vector3 Rotate(Vector3 point, float angle)
    {
        var sine = Math.Sin(angle); var cosine = Math.Cos(angle);
        return new((float)(point.X * cosine + point.Y * sine), (float)(-point.X * sine + point.Y * cosine), point.Z);
    }
}
