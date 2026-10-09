using System;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Static model-space preview bindings to the authored GRN rest pose.
/// FX attachment vector slots are distinct from GRN skeleton bone indices.</summary>
internal sealed record FxPreviewAttachmentInputs(Vector3 Point, ParticleEmissionLine? Line)
{
    public static bool TryCreate(GrnAsset model, SacredModelFxAttachmentDefinition rule,
        SacredParticleProjection projection, out FxPreviewAttachmentInputs? inputs)
    {
        inputs = null;
        var bones = model.Diagnostics?.Slices.FirstOrDefault()?.Bones;
        var start = bones?.FirstOrDefault(bone => bone.Name.Equals(rule.StartBoneName, StringComparison.OrdinalIgnoreCase));
        if (start is null) return false;
        var end = rule.EndBoneName is { } name ? bones!.FirstOrDefault(bone =>
            bone.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
        if (rule.EndBoneName is not null && end is null) return false;
        Vector3 Local(Vector3 position) => new(position.X / projection.HorizontalScale,
            -position.Y / (MathF.Sqrt(2) * projection.GroundDepthFactor * projection.VerticalScale),
            position.Z / (MathF.Sqrt(2) * projection.HeightFactor * projection.VerticalScale));
        var point = Local(start.Position);
        inputs = new(point, end is null ? null : new ParticleEmissionLine(point, Local(end.Position)));
        return true;
    }
}
