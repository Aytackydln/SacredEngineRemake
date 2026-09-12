using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

public static class EquipmentEffectSceneFactory
{
    public static EquipmentEffectScene? Create(GrnAsset asset, IReadOnlyList<EquipmentEffectAttachment> attachments)
    {
        if (asset.Diagnostics is not { } diagnostics || attachments.Count == 0) return null;
        var builder = new EffectMeshBuilder();
        foreach (var attachment in attachments)
        {
            if ((uint)attachment.ModelSliceIndex >= (uint)diagnostics.Slices.Count) continue;
            var slice = diagnostics.Slices[attachment.ModelSliceIndex];
            builder.BeginAttachment(attachment.RigidAttachBoneName);
            AddStandardEffects(builder, slice, attachment);
            var definition = SacredModelEffectCatalogue.Find(attachment.ItemId, attachment.BaseItemId);
            if (definition is not null)
            {
                AddNative(builder, slice, attachment, definition);
                Console.WriteLine($"[model effects] item={attachment.ItemId} kind={definition.Kind} texture={definition.TextureName} native=0x{definition.PredicateAddress:X}");
                // MAGICWHIP is the sole native predicate that exits before the
                // elemental-damage stage in cWeapon3D::toggleVisuals.
                if (definition.Kind == SacredModelEffectKind.Whip) continue;
            }
            AddMagicWeaponEffect(builder, slice, attachment);
            AddItemEffectBillboards(builder, slice, attachment);
            AddElemental(builder, slice, attachment);
        }
        return builder.Build();
    }

    public static EquipmentEffectScene? Create(
        GrnAsset asset,
        SacredEquipmentDamage damage,
        uint itemId = 0,
        uint baseItemId = 0,
        SacredEquipmentBonusTypes bonusTypes = default,
        SacredEquipmentBonusGroups bonusGroups = default,
        SacredEquipmentType equipmentType = default,
        byte itemEffectSelector = 0)
    {
        var boundsSize = asset.Diagnostics?.WholeModelBounds is { } bounds
            ? Vector3.Distance(bounds.Min, bounds.Max) : 40f;
        return Create(asset, [new EquipmentEffectAttachment(0, asset.Name, null, damage, boundsSize)
        {
            ItemId = itemId,
            BaseItemId = baseItemId,
            BonusTypes = bonusTypes,
            BonusGroups = bonusGroups,
            EquipmentType = equipmentType,
            ItemEffectSelector = itemEffectSelector
        }]);
    }

    private static void AddMagicWeaponEffect(
        EffectMeshBuilder builder,
        GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment)
    {
        var variant = SacredEquipmentMagicEffectSelector.Select(
            attachment.BonusTypes,
            attachment.BonusGroups,
            attachment.EquipmentType);
        if (variant == SacredEquipmentMagicEffectVariant.None)
            return;

        var anchor = Anchors(slice, SacredEquipmentEffectAnchorKind.Glow)
            .FirstOrDefault(bone => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var parsed) && parsed.Index == 1);
        if (anchor is null)
            return;

        var definition = SacredModelEffectCatalogue.FindMagicWeapon((byte)variant);
        if (definition is null)
            return;

        var boneName = attachment.RigidAttachBoneName ?? anchor.AnimationBoneName;
        builder.AddNativeEffect(definition, anchor.Position, anchor.Position, boneName, -Vector3.UnitY);
        if (definition.LensFlareTextureName is { } flare)
            builder.AddBillboard(anchor.Position, definition.HaloHalfSize * 2, definition.HaloHalfSize * 2,
                flare, NativeModelEffectSimulation.Unpack(definition.HaloColor),
                ParticleTextureMode.NativeLensFlare, boneName: boneName);
        Console.WriteLine($"[magic weapon effect] item={attachment.ItemId} variant={(byte)variant} anchor={anchor.Name} texture={definition.TextureName} native=0x{definition.PredicateAddress:X}");
    }

    private static void AddStandardEffects(
        EffectMeshBuilder builder,
        GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment)
    {
        var definition = SacredModelEffectCatalogue.StandardGlow;
        var anchors = Anchors(slice, SacredEquipmentEffectAnchorKind.StandardEffect)
            .TakeWhile((bone, index) => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor) &&
                anchor.Index == index + 1 && index < definition.MaximumAnchorCount);
        foreach (var anchor in anchors)
        {
            builder.AddBillboard(
                anchor.Position,
                definition.HalfSize * 2,
                definition.HalfSize * 2,
                definition.TextureName,
                NativeModelEffectSimulation.Unpack(definition.Color),
                ParticleTextureMode.NativeModel,
                boneName: attachment.RigidAttachBoneName ?? anchor.AnimationBoneName);
            Console.WriteLine($"[standard model effect] item={attachment.ItemId} anchor={anchor.Name} texture={definition.TextureName} native=0x{definition.NativeAddress:X}");
        }
    }

    private static void AddNative(EffectMeshBuilder builder, GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment, SacredModelEffectDefinition definition)
    {
        var anchors = Anchors(slice, definition.Kind == SacredModelEffectKind.Streak
            ? SacredEquipmentEffectAnchorKind.Streak : SacredEquipmentEffectAnchorKind.ElementalEmitter)
            .TakeWhile((bone, index) => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor) &&
                anchor.Index == index + 1 && index < (definition.Kind == SacredModelEffectKind.Streak ? 10 : 3)).ToArray();
        if (anchors.Length == 0) return;
        Console.WriteLine($"[model effects] item={attachment.ItemId} base={attachment.BaseItemId} anchors={anchors.Length}: {string.Join(", ", anchors.Select(anchor => anchor.Name))}");
        var start = anchors[0].Position;
        var end = anchors.Length > 1 ? anchors[1].Position : start;
        if (definition.Kind == SacredModelEffectKind.Streak)
        {
            foreach (var anchor in anchors)
                builder.AddNativeEffect(definition, anchor.Position, anchor.Position,
                    attachment.RigidAttachBoneName ?? anchor.AnimationBoneName, anchor.Direction);
        }
        else if (definition.Kind != SacredModelEffectKind.Beam)
            builder.AddNativeEffect(definition, start,
                definition.Kind == SacredModelEffectKind.Worms ? end : start,
                attachment.RigidAttachBoneName ?? anchors[0].AnimationBoneName, -Vector3.UnitY);

        // TORCHSMOKE::render follows stdRender with stdLensflare(type 3), centered
        // on the system. Type 3 selects PARTICLE_GLOW01.TGA in the native switch.
        if (definition.Kind == SacredModelEffectKind.Torch && definition.LensFlareTextureName is { } flareTexture)
            builder.AddBillboard(start, definition.HalfSize * 2, definition.HalfSize * 2,
                flareTexture, NativeModelEffectSimulation.Unpack(definition.Color), ParticleTextureMode.NativeModel);

        if (definition.Kind is SacredModelEffectKind.Beam or SacredModelEffectKind.Worms)
            builder.AddNativeBeam(definition, start, end);
    }

    private static GrnBoneDiagnostics[] Anchors(GrnSliceDiagnostics slice, SacredEquipmentEffectAnchorKind kind) =>
        slice.Bones.Where(bone => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor) && anchor.Kind == kind)
            .GroupBy(bone => bone.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(bone => bone.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static void AddItemEffectBillboards(EffectMeshBuilder builder, GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment)
    {
        // renderItemEffects compares the Items.pak descriptor selector with 9,
        // then probes these four literal model helpers. The native half-size is
        // 3 + rand()/32768; use its stable midpoint for retained scene geometry.
        if (attachment.ItemEffectSelector != 9)
            return;

        var anchors = Anchors(slice, SacredEquipmentEffectAnchorKind.ItemEffectBillboard)
            .TakeWhile((bone, index) => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var parsed) &&
                parsed.Index == index && index < 4);
        foreach (var anchor in anchors)
        {
            builder.AddBillboard(anchor.Position, 7f, 7f,
                SacredModelEffectCatalogue.StandardGlow.TextureName, Vector4.One,
                ParticleTextureMode.NativeModel,
                boneName: attachment.RigidAttachBoneName ?? anchor.AnimationBoneName);
        }
    }

    private static void AddElemental(EffectMeshBuilder builder, GrnSliceDiagnostics slice,
        EquipmentEffectAttachment attachment)
    {
        if (ElementalWeaponEffectSelector.Select(attachment.Damage) is not { } selected) return;
        var anchors = Anchors(slice, SacredEquipmentEffectAnchorKind.ElementalEmitter)
            .TakeWhile((bone, index) => SacredEquipmentEffectAnchor.TryParse(bone.Name, out var anchor) &&
                anchor.Index == index + 1 && index < 3).ToArray();
        if (anchors.Length == 0) return;
        var effect = selected.Definition;
        var halfSize = effect.GlowHalfSizeOffset + effect.GlowHalfSizeScale * selected.Intensity;
        for (var index = 0; index < Math.Max(1, anchors.Length - 1); index++)
        {
            var next = Math.Min(index + 1, anchors.Length - 1);
            builder.AddNativeGlowLine(anchors[index].Position, anchors[next].Position,
                effect.TextureName, effect.GlowColor, halfSize, effect.GlowDensity);
        }
        if (effect.ParticleTypeId != 0)
            builder.AddNativeEffect(effect.ParticleDefinition(selected.Intensity), anchors[0].Position,
                anchors[^1].Position, attachment.RigidAttachBoneName ?? anchors[0].AnimationBoneName, -Vector3.UnitY);
        Console.WriteLine($"[elemental weapon effects] item={attachment.ItemId} kind={effect.Kind} intensity={selected.Intensity:R} particle=0x{effect.ParticleTypeId:X} native=0x{effect.NativeAddress:X}");
    }
}
