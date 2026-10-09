using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Viewer scene inputs, separate from native particle parameters.
/// Repeated samples illustrate a point effect along a segment; sample spacing
/// does not claim to reproduce the game's combat-event scheduling.</summary>
internal sealed record FxPreviewParameters(float? Distance = null)
{
    public const float MaximumDistance = 2000;
    public const float DefaultPointDistance = 200;
    private const int PointSamples = 5;

    // The constructor identifies the decoded family, without item/name/texture IDs.
    public static bool UsesPointSegment(SacredParticleDefinition definition) =>
        definition.NativeConstructorAddress == 0x79DC90 && definition.IsEventPreset;

    public static bool UsesDistance(SacredParticleDefinition definition) =>
        definition.LineEmission is not null || UsesPointSegment(definition);

    public static FxPreviewParameters Default(SacredParticleDefinition definition) =>
        new(UsesPointSegment(definition) ? DefaultPointDistance : null);

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

    public IReadOnlyList<Vector3> Origins(SacredParticleDefinition definition)
    {
        Validate();
        if (!UsesPointSegment(definition)) return [Vector3.Zero];
        var distance = Distance ?? DefaultPointDistance;
        if (distance == 0) return [Vector3.Zero];
        var points = new Vector3[PointSamples];
        for (var i = 0; i < points.Length; i++) points[i] = new(distance * i / (points.Length - 1), 0, 0);
        return points;
    }
}
