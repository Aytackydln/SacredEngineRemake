using System;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal static class FxPreviewDeflectionInputs
{
    public static Vector3 Resolve(GrnAsset actor, SacredDeflectionFxDefinition definition, SacredParticleProjection projection)
    {
        var rule = SacredFxAttachments.Find(definition.AttachmentSubtype)
            ?? throw new InvalidOperationException("The deflection creation attachment is not decoded.");
        foreach (var slot in new ushort?[] { rule.Bone, rule.FallbackBone })
        {
            if (slot is not { } index || SacredFxAttachments.BoneName(index) is not { } name) continue;
            var bone = actor.Diagnostics?.Slices.SelectMany(slice => slice.Bones)
                .FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (bone is null) continue;
            var position = bone.Position;
            var point = new Vector3(position.X / projection.HorizontalScale,
                -position.Y / (MathF.Sqrt(2) * projection.GroundDepthFactor * projection.VerticalScale),
                position.Z / (MathF.Sqrt(2) * projection.HeightFactor * projection.VerticalScale));
            Console.WriteLine($"[Assets] Deflection attachment: {name}; model point {position}; native point {point}.");
            return point;
        }
        throw new InvalidOperationException("This actor has neither the native deflection bone nor its root fallback.");
    }
}
