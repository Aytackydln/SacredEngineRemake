using System;
using System.Numerics;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Viewer scene inputs, separate from native particle parameters.
/// Cast trails use the recovered combat caller, independently of the named FX layer.</summary>
internal sealed record FxPreviewParameters(float? Distance = null, bool UseCastTrail = true)
{
    public const float MaximumDistance = 2000;
    public const float DefaultPointDistance = 200;
    public static bool UsesPointSegment(SacredParticleDefinition definition) =>
        definition.CastTrail is not null;

    public static bool UsesDistance(SacredParticleDefinition definition) =>
        definition.LineEmission is not null || definition.Anducar is not null || UsesPointSegment(definition);

    public static FxPreviewParameters Default(SacredParticleDefinition definition) =>
        new(UsesPointSegment(definition) ? DefaultPointDistance : definition.Anducar is not null ? 0 : null);

    public void Validate()
    {
        if (Distance is { } distance && (!float.IsFinite(distance) || distance < 0 || distance > MaximumDistance))
            throw new ArgumentOutOfRangeException(nameof(Distance), $"FX distance must be between 0 and {MaximumDistance} native units.");
    }

    public ParticleEmissionLine ResolveLine(ParticleEmissionLine authored)
    {
        Validate();
        if (Distance is not { } distance) return authored;
        var delta = authored.End - authored.Start;
        var direction = delta.LengthSquared() > 0 ? Vector3.Normalize(delta) : Vector3.UnitZ;
        return new(authored.Start, authored.Start + direction * distance);
    }
}
