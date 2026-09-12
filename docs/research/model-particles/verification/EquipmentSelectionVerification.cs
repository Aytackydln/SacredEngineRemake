using System.Buffers.Binary;
using System.Numerics;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Animation;
using Sacred.Inventory.Effects;
using Sacred.Particles;

internal static class EquipmentSelectionVerification
{
    public static void Run(string pak, IReadOnlyDictionary<ushort, ItemsPakEntry> items, SacredEquipment[] equipment)
    {
        var bytes = File.ReadAllBytes(Path.Combine(pak, "Weapon.pak"));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        Check(equipment.Length == count && count == 4883, "all native weapon records, including the eleven formerly skipped");
        for (var i = 0; i < equipment.Length; i++)
        {
            var raw = bytes.AsSpan(0x100 + i * 0x102, 0x102);
            var e = equipment[i];
            Check(e.IdemId == BinaryPrimitives.ReadUInt32LittleEndian(raw[0x80..]), "native record identity");
            Check(e.BaseItemId == BinaryPrimitives.ReadUInt32LittleEndian(raw[0x24..]), "native base visual");
            Check(e.PreviewScale == BitConverter.ToSingle(raw), "native preview scale");
            for (var slot = 0; slot < 8; slot++)
            {
                Check(e.BonusTypes[slot] == BinaryPrimitives.ReadUInt16LittleEndian(raw[(0xBA + slot * 2)..]),
                    $"BonusT[{slot}] layout");
                Check(e.BonusGroups[slot] == BinaryPrimitives.ReadUInt32LittleEndian(raw[(0xCA + slot * 4)..]),
                    $"BonusG[{slot}] layout");
                Check(e.BonusValues[slot] == BinaryPrimitives.ReadInt16LittleEndian(raw[(0xEA + slot * 2)..]),
                    $"BonusP[{slot}] layout");
            }
            if (e.BaseItemId == 0) continue;
            Check(e.Item.ItemIndex == e.IdemId, "derived descriptor row identity");
            var rawBase = items[(ushort)e.BaseItemId].ModelName;
            if (!string.IsNullOrEmpty(rawBase))
                Check(e.Item.ModelName == rawBase, $"inherited model {e.IdemId}");
        }
        var native = equipment.Where(e => SacredModelEffectCatalogue.Find(e.IdemId, e.BaseItemId) != null).ToArray();
        Check(native.Length == 24, "eight direct and sixteen inherited native-effect items");
        foreach (var e in native)
            Console.WriteLine($"SELECT {e.IdemId} base={e.BaseItemId}: {SacredModelEffectCatalogue.Find(e.IdemId, e.BaseItemId)!.Kind}; {e.Name}; {e.Item.ModelName}");
        Check(SacredModelEffectCatalogue.Find(3072)!.Kind == SacredModelEffectKind.Beam, "native beam predicate");
        Check(SacredModelEffectCatalogue.Find(3073)!.Kind == SacredModelEffectKind.Whip, "native whip predicate");
        Check(SacredModelEffectCatalogue.Find(3809)!.Kind == SacredModelEffectKind.Beam, "native Seraphim BFG glow line");
        Check(SacredModelEffectCatalogue.Find(12345, 5632) == null, "torch predicate does not inherit");
        CheckMagic(equipment, 3112, SacredEquipmentMagicEffectVariant.Violet);
        CheckMagic(equipment, 3116, SacredEquipmentMagicEffectVariant.Blue);
        CheckMagic(equipment, 1876, SacredEquipmentMagicEffectVariant.Violet);
        CheckMagic(equipment, 1725, SacredEquipmentMagicEffectVariant.None);
        CheckMagic(equipment, 1742, SacredEquipmentMagicEffectVariant.None);
        CheckMagic(equipment, 1851, SacredEquipmentMagicEffectVariant.None);
        Check(SacredEquipmentEffectAnchor.TryParse("sera03_fx0", out var itemEffectAnchor) &&
              itemEffectAnchor.Kind == SacredEquipmentEffectAnchorKind.ItemEffectBillboard &&
              itemEffectAnchor.Index == 0,
            "zero-based Items.pak effect helper convention");
        Check(SacredModelEffectCatalogue.MagicWeaponDefinitions.Count == 5,
            "all FIREBALL colour variants extracted");
        foreach (var definition in SacredModelEffectCatalogue.MagicWeaponDefinitions)
        {
            Check(definition.TextureName.Equals("PARTICLE_GLOW01.TGA", StringComparison.OrdinalIgnoreCase),
                "FIREBALL native texture");
            Check(string.Equals(definition.LensFlareTextureName, "PARTICLE_FLARE02.TGA", StringComparison.OrdinalIgnoreCase),
                "FIREBALL native lens-flare texture");
            Check(definition.HaloColor == 0xFFFFFFFFu, "FIREBALL lens-flare tint is white");
            Check(definition.ParticleColors.Count == 256, "FIREBALL energy colour table");
        }
        Check(SacredModelEffectCatalogue.StandardGlow.TextureName.Equals(
            "PARTICLE_GLOW03.TGA", StringComparison.OrdinalIgnoreCase), "standard model billboard texture");
        Console.WriteLine($"PASS weapon layout and native selection: {equipment.Length} records, {native.Length} effect items");
    }

    public static async Task CheckInheritedModels(ModelsPakArchive models, string character, SacredEquipment[] equipment)
    {
        var elementalKinds = new HashSet<SacredElementalWeaponEffectKind>();
        foreach (var e in equipment.Where(e => e.BaseItemId != 0 && SacredModelEffectCatalogue.Find(e.IdemId, e.BaseItemId) != null))
        {
            var d = SacredModelEffectCatalogue.Find(e.IdemId, e.BaseItemId)!;
            var wing = d.Kind == SacredModelEffectKind.Streak;
            var model = await models.LoadCharacterBaseModelAsync(character,
                [new(e.Item.ModelName, wing ? null : "Bip01 R Hand", wing ? null : "Bone_weapon_01")]);
            var scene = EquipmentEffectSceneFactory.Create(model,
                [new EquipmentEffectAttachment(1, e.Item.ModelName, wing ? null : "Bip01 R Hand", e.Damage, 40)
                {
                    ItemId = e.IdemId,
                    BaseItemId = e.BaseItemId,
                    BonusTypes = e.BonusTypes,
                    BonusGroups = e.BonusGroups,
                    EquipmentType = e.EquipmentType,
                    ItemEffectSelector = e.Item.ModelDesc.EffectTextureIndex
                }]);
            Check(scene is not null && scene.Mesh.Vertices.Length > 0, $"inherited effect geometry {e.IdemId}");
            if (ElementalWeaponEffectSelector.Select(e.Damage) is { } elemental)
            {
                var expected = Color(elemental.Definition.GlowColor);
                Check(scene!.Surfaces.Any(surface => surface.TextureName.Equals(
                          elemental.Definition.TextureName, StringComparison.OrdinalIgnoreCase) &&
                      Vector4.Distance(surface.Color, expected) < 0.00001f),
                    $"elemental {elemental.Definition.Kind} effect geometry {e.IdemId}");
                elementalKinds.Add(elemental.Definition.Kind);
            }
        }
        Check(elementalKinds.Count == 3, "fire, magic, and poison equipment effects remain active");
        Console.WriteLine("PASS all sixteen inherited native effects and all three elemental paths create model geometry");

        static Vector4 Color(uint packed) => new(
            (packed >> 16 & 255) / 255f,
            (packed >> 8 & 255) / 255f,
            (packed & 255) / 255f,
            (packed >> 24) / 255f);
    }

    public static async Task CheckRequestedEffects(
        ModelsPakArchive models,
        string character,
        SacredEquipment[] equipment)
    {
        var clip = await models.LoadCharacterAnimationAsync(
            character, CharacterMotionKind.Idle, CharacterMotionWeaponStyle.OneHanded);
        Check(clip is not null, "requested-effect animation");
        foreach (var id in new uint[] { 1876, 3112, 3116 })
        {
            var item = equipment.Single(candidate => candidate.IdemId == id);
            var model = await models.LoadCharacterBaseModelAsync(character,
                [new(item.Item.ModelName, "Bip01 R Hand", "Bone_weapon_01")]);
            var scene = EquipmentEffectSceneFactory.Create(model,
                [Attachment(item, "Bip01 R Hand")]);
            Check(scene is not null, $"magic weapon scene {id}");
            var flare = scene!.Surfaces.Single(surface => surface.TextureName.Equals(
                "PARTICLE_FLARE02.TGA", StringComparison.OrdinalIgnoreCase));
            Check(flare.Color == Vector4.One && flare.TextureMode == ParticleTextureMode.NativeLensFlare,
                $"magic weapon {id} uses authored RGB flare with white tint");
            Check(scene.Surfaces.Any(surface => surface.TextureName.Equals(
                "PARTICLE_GLOW01.TGA", StringComparison.OrdinalIgnoreCase)), $"magic weapon trail {id}");
            var flareVertex = scene.Mesh.Indices[flare.IndexStart];
            var before = scene.Mesh.Vertices[flareVertex].Position;
            var pose = new GrnAnimatedMesh(model.Mesh!, model.Skin!, clip!);
            pose.Apply(0.5f);
            scene.ApplyPose(pose, 1f / 60);
            Check(Vector3.Distance(before, scene.Mesh.Vertices[flareVertex].Position) > 0.001f,
                $"magic weapon flare follows animated model {id}");
        }

        var wings = equipment.Single(candidate => candidate.IdemId == 3111);
        var wingModel = await models.LoadModelAsync(wings.Item.ModelName);
        var wingScene = EquipmentEffectSceneFactory.Create(wingModel, [Attachment(wings, null, 0)]);
        Check(wingScene is not null && wingScene.Surfaces.Where(surface =>
                    surface.TextureName.Equals("PARTICLE_GLOW03.TGA", StringComparison.OrdinalIgnoreCase) &&
                    surface.Color == Vector4.One)
                .Sum(surface => surface.IndexCount) == 4 * 6,
            "Items.pak effect selector 9 creates four white sera03_fx billboards");
        Console.WriteLine("PASS requested 1876/3112/3116 trails and 3111 item-selector billboards");

        static EquipmentEffectAttachment Attachment(SacredEquipment item, string? boneName, int sliceIndex = 1) =>
            new(sliceIndex, item.Item.ModelName, boneName, item.Damage, 40)
            {
                ItemId = item.IdemId,
                BaseItemId = item.BaseItemId,
                BonusTypes = item.BonusTypes,
                BonusGroups = item.BonusGroups,
                EquipmentType = item.EquipmentType,
                ItemEffectSelector = item.Item.ModelDesc.EffectTextureIndex
            };
    }

    private static void CheckMagic(
        IEnumerable<SacredEquipment> equipment,
        uint itemId,
        SacredEquipmentMagicEffectVariant expected)
    {
        var item = equipment.Single(candidate => candidate.IdemId == itemId);
        Check(SacredEquipmentMagicEffectSelector.Select(item.BonusTypes, item.BonusGroups, item.EquipmentType) == expected,
            $"model magic effect {itemId} selects {expected}");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"FAILED: {label}");
    }
}
