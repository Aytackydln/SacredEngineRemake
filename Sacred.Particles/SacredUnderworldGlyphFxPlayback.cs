using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native clocks, stored randomized waves, texture scrolling and ribbon geometry.</summary>
public sealed class SacredUnderworldGlyphFxPlayback
{
    private SacredUnderworldGlyphFxStateLayout _state;
    private readonly SacredFxSurfaceVertexLayout[] _ribbons = new SacredFxSurfaceVertexLayout[360];

    public SacredUnderworldGlyphFxPlayback(SacredUnderworldGlyphFxDefinition definition, Func<int> random)
    {
        Definition = definition;
        Restart(random);
    }
    public SacredUnderworldGlyphFxDefinition Definition { get; }
    public SacredUnderworldGlyphFxStateLayout CaptureState() => _state;
    public SacredFxSurfaceVertexLayout RibbonVertex(int wave, int vertex) => _ribbons[wave * Definition.RibbonVertices.Count + vertex];

    public void Restart(Func<int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _state = new() { Active = 1, Element = Definition.Element, Thickness = Definition.InitialThickness };
        for (var wave = 0; wave < SacredUnderworldGlyphFxStateLayout.WaveCount; wave++)
        {
            _state.WaveRates[wave] = (float)(Definition.WaveRateOrigin - random() * (double)Definition.RandomScale * Definition.WaveRateWidth);
            _state.WaveRadii[wave] = (float)(random() * (double)Definition.RandomScale * Definition.WaveRadiusWidth + Definition.WaveRadiusOrigin);
        }
        // Initialization leaves all points zero until the first advanceTime call.
        SacredGlyphRibbonGeometry.Write(in _state, Definition, _ribbons);
    }

    public void RestoreState(in SacredUnderworldGlyphFxStateLayout state)
    {
        if (state.Element != Definition.Element) throw new ArgumentException("Glyph snapshot requires the matching model definition.", nameof(state));
        _state = state;
        SacredGlyphRibbonGeometry.Write(in _state, Definition, _ribbons);
    }

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var waveTime = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)waveTime;
        var rotation = _state.Rotation + (double)seconds;
        _state.Rotation = (float)(rotation > Definition.FullTurn ? rotation - Definition.FullTurn : rotation);
        for (var wave = 0; wave < SacredUnderworldGlyphFxStateLayout.WaveCount; wave++)
        for (var point = 0; point < SacredUnderworldGlyphFxStateLayout.PointsPerWave; point++)
        {
            var angle = (float)((waveTime * _state.WaveRates[wave] + point * (double)Definition.PointPhaseStep) * Definition.HalfTurn);
            var radius = _state.WaveRadii[wave];
            _state.Points[wave * SacredUnderworldGlyphFxStateLayout.PointsPerWave + point] = new(
                (float)(Math.Cos(angle) * radius * Definition.WaveRadiusScale),
                (float)(Math.Sin(angle) * radius * Definition.WaveRadiusScale), (wave + Definition.WaveHeightOrigin) * 2);
        }
        SacredGlyphRibbonGeometry.Write(in _state, Definition, _ribbons);
    }

    public Vector3 ModelPosition(Vector3 nativeVertex)
    {
        var point = nativeVertex * Definition.ModelScale;
        var sine = Math.Sin(_state.Rotation); var cosine = Math.Cos(_state.Rotation);
        return new((float)(point.X * cosine + point.Y * sine), (float)(-point.X * sine + point.Y * cosine), point.Z + Definition.ModelHeight);
    }

    public Vector2 ModelTextureCoordinate(int pass, Vector2 authored)
    {
        if ((uint)pass > 1) throw new ArgumentOutOfRangeException(nameof(pass));
        var uRate = pass == 0 ? Definition.FirstUScrollRate : Definition.SecondUScrollRate;
        var vRate = pass == 0 ? Definition.FirstVScrollRate : Definition.SecondVScrollRate;
        return authored + new Vector2((float)Math.Sin(_state.ElapsedTime * (double)uRate), (float)Math.Cos(_state.ElapsedTime * (double)vRate));
    }
}
