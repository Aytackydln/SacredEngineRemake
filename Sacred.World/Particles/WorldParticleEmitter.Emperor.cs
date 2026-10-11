using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private readonly SacredEmperorFxPlayback? _emperor;
    internal bool RequestEmperorHit(uint kind)
    {
        if (_emperor is null) return false;
        _emperor.RequestHit(kind); return true;
    }
    private void UpdateEmperor(float seconds, List<WorldParticle> output)
    {
        var emperor = _emperor!;
        _emissionTime = _age;
        UpdateParticles(seconds);
        var oldEmission = emperor.Emission;
        // Births use the parameters from before this update's hit processing.
        if (emperor.Emitting) EmitParticles(seconds);
        emperor.Advance(seconds, _particleCount > 0);
        _age = emperor.ElapsedTime;
        NativeRetired = !emperor.Active;
        if (oldEmission.Size != emperor.Emission.Size || oldEmission.SizeRandomWidth != emperor.Emission.SizeRandomWidth)
        {
            _parameterSets[0] = _parameterSets[0] with { Emission = emperor.Emission };
            _batches[0].ApplyParameters(_parameterSets[0]);
        }
        if (NativeRetired) return;
        AppendDraw(_placement.Definition.Draw!, output);
        if (emperor.RenderHalfSize() is { } size) AppendEmperorFlare(size, output);
    }
    private void AppendEmperorFlare(float halfSize, List<WorldParticle> output)
    {
        var definition = _placement.Definition.Emperor!;
        var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
        var height = (_heightOffset ?? _placement.Creation.HeightOffset ?? 0) * _projection.HeightFactor * _projection.VerticalScale;
        // Standalone playback has a zero base direction; the native flare offset is -Direction.
        output.Add(new(_placement.ScriptOffset,
            new(definition.FlareTexture, 1, 1, 1, 1, ParticleShaderKind.ItemParticle),
            origin.X, origin.Y, height, 2 * halfSize * _projection.HorizontalScale, (definition.FlareColor >> 24) / 255f, -1)
        {
            Color = definition.FlareColor, Additive = true, SourceColorOnly = definition.FlareSourceColorOnly,
            RenderHeight = 2 * halfSize * _projection.VerticalScale,
            PainterDepthKey = WorldPainterDepth.FromWorld(origin) + height / IsometricProjection.StepWidth
        });
    }
}
