using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

public static class EquipmentEffectSceneFactory
{
    private const string FireTexture = "PARTICLE_FIRE02.TGA";
    private const string OrbTexture = "PARTICLE_GLOW06.TGA";
    private const string BouncyLineTexture = "FX_STREAKS01.TGA";

    public static EquipmentEffectScene? Create(
        GrnAsset asset,
        IReadOnlyList<EquipmentEffectAttachment> attachments)
    {
        if (asset.Diagnostics is not { } diagnostics || attachments.Count == 0)
            return null;

        var builder = new EffectMeshBuilder();
        foreach (var attachment in attachments)
        {
            if ((uint)attachment.ModelSliceIndex >= (uint)diagnostics.Slices.Count)
                continue;

            AddAttachment(
                diagnostics.Slices[attachment.ModelSliceIndex],
                attachment,
                builder);
        }

        return builder.Build();
    }

    public static EquipmentEffectScene? Create(GrnAsset asset, SacredEquipmentDamage damage)
    {
        var boundsSize = asset.Diagnostics?.WholeModelBounds is { } bounds
            ? Vector3.Distance(bounds.Min, bounds.Max)
            : 40.0f;
        return Create(asset, [new EquipmentEffectAttachment(0, asset.Name, null, damage, boundsSize)]);
    }

    private static void AddAttachment(
        GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment,
        EffectMeshBuilder builder)
    {
        var points = slice.Bones
            .Select(static bone => TryCreatePoint(bone, out var point) ? point : (EffectAnchorPoint?)null)
            .Where(static point => point.HasValue)
            .Select(static point => point!.Value)
            .GroupBy(static point => point.Anchor.BoneName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (points.Length == 0)
            return;

        builder.BeginAttachment(attachment.RigidAttachBoneName);
        var boundsSize = float.IsFinite(attachment.ModelBoundsSize) && attachment.ModelBoundsSize > 0.0f
            ? attachment.ModelBoundsSize
            : 40.0f;
        var unit = Math.Clamp(boundsSize * 0.035f, 1.5f, 5.0f);
        var elements = GetElements(attachment.Damage).ToArray();
        var emitters = OfKind(points, SacredEquipmentEffectAnchorKind.ElementalEmitter);
        var primaryEmitter = emitters.FirstOrDefault();
        var isTorch = attachment.ModelName.StartsWith("TORCH_", StringComparison.OrdinalIgnoreCase);

        if (isTorch && attachment.Damage.Fire.IsPresent && primaryEmitter != default)
        {
            var flameWidth = Math.Max(16.0f, boundsSize * 0.65f);
            var flameHeight = Math.Max(25.0f, boundsSize * 0.95f);
            builder.AddBillboard(
                primaryEmitter.Position + Vector3.UnitX * (flameHeight * 0.5f) + Vector3.UnitZ * (flameHeight * 0.5f),
                flameWidth,
                flameHeight,
                FireTexture,
                new Vector4(1.0f, 0.72f, 0.42f, 0.94f),
                ParticleTextureMode.Atlas4X4);
        }
        else if (primaryEmitter != default)
        {
            foreach (var element in elements)
                ElementalEffectFieldBuilder.Add(
                    builder,
                    emitters[0].Position,
                    emitters.Length > 1 ? emitters[1].Position : emitters[0].Position,
                    element,
                    unit,
                    slice.SurfaceTriangles);
        }

        var dominantColor = elements.FirstOrDefault().Color;
        if (dominantColor == default)
            dominantColor = new Vector4(0.45f, 0.72f, 1.0f, 0.82f);

        var standardEffects = OfKind(points, SacredEquipmentEffectAnchorKind.StandardEffect);
        var streaks = OfKind(points, SacredEquipmentEffectAnchorKind.Streak);
        if (streaks.Length == 0)
        {
            foreach (var standard in standardEffects)
                builder.AddBillboard(
                    standard.Position,
                    unit * 6.5f,
                    unit * 6.5f,
                    OrbTexture,
                    new Vector4(0.42f, 0.66f, 1.0f, 0.78f),
                    ParticleTextureMode.Luminance);
        }

        if (standardEffects.Length == 0)
        {
            var glows = OfKind(points, SacredEquipmentEffectAnchorKind.Glow);
            if (glows.Length > 0)
                WeaponGlowEffectBuilder.Add(builder, glows[0].Position, unit);
            for (var index = 1; index < glows.Length; index++)
                WeaponGlowEffectBuilder.AddTrail(
                    builder,
                    glows[index - 1].Position,
                    glows[index].Position,
                    unit);
        }

        var streakOrigin = standardEffects.FirstOrDefault();
        if (streakOrigin != default)
        {
            var streakColor = new Vector4(0.58f, 0.78f, 1.0f, 0.72f);
            foreach (var streak in streaks)
            {
                var away = streak.Position - streakOrigin.Position;
                if (away.LengthSquared() < 0.0001f)
                    continue;
                builder.AddCrossedStrip(
                    streak.Position,
                    streak.Position + Vector3.Normalize(away) * (unit * 5.0f),
                    unit * 0.62f,
                    BouncyLineTexture,
                    streakColor,
                    ParticleTextureMode.BouncyAlpha);
            }
        }

        AddModelEmitterStreak(slice, primaryEmitter, unit, dominantColor, builder);
    }

    private static void AddModelEmitterStreak(
        GrnSliceDiagnostics slice,
        EffectAnchorPoint primaryEmitter,
        float unit,
        Vector4 color,
        EffectMeshBuilder builder)
    {
        if (primaryEmitter == default)
            return;

        var cylinder = slice.Bones.FirstOrDefault(
            static bone => bone.Name.Equals("Cylinder01", StringComparison.OrdinalIgnoreCase));
        if (cylinder is null)
            return;

        var away = primaryEmitter.Position - cylinder.Position;
        if (away.LengthSquared() < 0.0001f)
            away = Vector3.UnitZ;
        builder.AddCrossedStrip(
            primaryEmitter.Position,
            primaryEmitter.Position + Vector3.Normalize(away) * (unit * 6.0f),
            unit * 0.68f,
            BouncyLineTexture,
            color,
            ParticleTextureMode.BouncyAlpha);
    }

    private static EffectAnchorPoint[] OfKind(
        IEnumerable<EffectAnchorPoint> points,
        SacredEquipmentEffectAnchorKind kind) =>
        points.Where(point => point.Anchor.Kind == kind)
            .OrderBy(static point => point.Anchor.Index)
            .ToArray();

    private static IEnumerable<ElementEffect> GetElements(SacredEquipmentDamage damage)
    {
        if (damage.Fire.IsPresent)
            yield return new ElementEffect(ElementKind.Fire, new Vector4(1.0f, 0.533f, 0.267f, 0.251f));
        if (damage.Magic.IsPresent)
            yield return new ElementEffect(ElementKind.Magic, new Vector4(0.439f, 0.20f, 1.0f, 0.314f));
        if (damage.Poison.IsPresent)
            yield return new ElementEffect(ElementKind.Poison, new Vector4(0.267f, 1.0f, 0.267f, 0.251f));
    }

    private static bool TryCreatePoint(GrnBoneDiagnostics bone, out EffectAnchorPoint point)
    {
        if (SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor))
        {
            point = new EffectAnchorPoint(anchor, bone.Position);
            return true;
        }

        point = default;
        return false;
    }

    private readonly record struct EffectAnchorPoint(SacredEquipmentEffectAnchor Anchor, Vector3 Position);
}
