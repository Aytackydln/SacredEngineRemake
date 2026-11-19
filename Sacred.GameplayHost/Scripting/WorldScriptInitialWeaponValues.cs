using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Entered-hero CreateObj ordinary weapon branch at 487AB0..487EB8.
/// The caller supplies the actual hero level only after the zero sector-maximum
/// gate. Authored rescaling and physical resource replacement stay guarded.</summary>
internal static class WorldScriptInitialWeaponValues
{
    public static void Apply(WorldEquipmentInitialItem item, uint heroResource, ushort level, uint difficulty,
        SacredEquipmentGenerationData data, SacredRandomSequence random)
    {
        var resource = item.Physical.Base.ObjectType;
        if (item.Generated || !item.Node.Active || (item.Physical.Base.Flags & 2) == 0 || item.Physical.AttachmentParent != 0 ||
            data.Templates.GetItem(resource).ModelDesc.Category != SacredItemCategory.Weapon)
            throw new NotSupportedException("CreateObj generation requires its actual fresh unplaced weapon.");
        var modifier = data.Modifiers.Find(resource);
        if (modifier is null || modifier.Definition.SelectionLimitValue == 15)
            throw new NotSupportedException("CreateObj weapon requires its remaining authored/no-modifier value caller.");
        var values = item.Values.ToArray();
        var setType = data.Templates.TryGetSharedTemplate(resource, out var shared) ? shared.SetType : (byte?)null;
        _ = SacredEquipmentVariantNormalization.Apply(values, resource, heroResource, setType,
            () => new WorldEquipmentVariants(data).Resolve(resource, heroResource),
            _ => throw new NotSupportedException("CreateObj weapon requires its actual resource replacement/model reset."),
            () => BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4)));
        // This caller draws QUALITY first, then legacy argument four. The NPC
        // equipment command uses a different order and is deliberately separate.
        var quality = Math.Min(10u, SacredEquipmentRaritySelection.Roll(
            (data.Tuning.Magic ?? throw new InvalidDataException("Original CreateObj rarity weights unavailable.")).RarityWeights, random));
        var legacy = (uint)(random.Next() % 4);
        var generator = new WorldEquipmentOrdinaryGeneration(data)
            { Naming = new WorldEquipmentNames(data), Precision = SacredX87Precision.Single };
        values = generator.Generate(resource, level, difficulty, values, random, quality);
        var name = generator.GeneratedName ?? throw new InvalidDataException("CreateObj weapon needs actual object-context naming.");
        item.Values = values;
        item.Name = name;
        item.Physical.Base = item.Physical.Base with { Word3C = name.NativeReference };
        item.Quality = quality; item.LegacyGenerationSelector = legacy; item.Generated = true;
    }
}
