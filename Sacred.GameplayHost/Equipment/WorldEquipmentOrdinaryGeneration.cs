using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Fresh ordinary Gold 0x5D5080 construction with native magic quality and
/// optional native naming before pricing. Native book/rune/horse early returns remain
/// explicit. Generated values are independent of later ownership/installation.</summary>
public sealed class WorldEquipmentOrdinaryGeneration(SacredEquipmentGenerationData data)
{
    public WorldEquipmentNames? Naming { private get; init; }
    public SacredEquipmentNameResult? GeneratedName { get; private set; }

    public byte[] Generate(uint type, ushort inputLevel, uint difficulty, ReadOnlySpan<byte> initialValues, SacredRandomSequence random, uint quality = 0)
    {
        GeneratedName = null;
        if (initialValues.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(initialValues));
        if (difficulty > 4) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var basis = type;
        for (var i = 0; i < data.Classes.MaximumParentLinks && data.Classes.ParentResources.Any(range => range.Contains(basis)); i++)
        {
            var parent = data.ParentResource(basis);
            if (!parent.HasValue) { basis = 0; break; }
            basis = parent.Value;
        }
        var modifier = data.Modifiers.Find(basis);
        var originalCategory = data.Templates.GetItem(BinaryPrimitives.ReadUInt32LittleEndian(initialValues)).ModelDesc.Category;
        if (modifier is null || originalCategory is SacredItemCategory.Book or SacredItemCategory.Rune || modifier.Definition.WeaponType == 29)
            throw new InvalidOperationException("The native ordinary generator retains this item's existing values; a separate generation path is required.");
        var typeCategory = data.Templates.GetItem(type).ModelDesc.Category;
        var category = data.Templates.GetItem(basis).ModelDesc.Category;
        var flags = typeCategory is SacredItemCategory.Weapon or SacredItemCategory.Shield or SacredItemCategory.Ring
            ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(initialValues[4..]) & 0xC0003F;
        flags |= (unchecked((uint)modifier.Definition.WeaponType) & 255) << 8;
        var result = new byte[SacredEquipmentInstanceLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(result, basis);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), flags);
        var tuning = data.Tuning;
        var level = tuning.Levels.ResolveLevel(inputLevel, modifier.Definition.MinimumLevel);
        SacredEquipmentCombatGeneration.Apply(result, modifier.Definition, level.CombatFactor, difficulty, random);
        SacredEquipmentEligibilityGeneration.Apply(result, modifier.Definition, level.Level, random);
        _ = SacredEquipmentAuthoredBonusGeneration.Apply(result, modifier.Bonuses, level.Level, difficulty, tuning.Bonuses, random, data.CombatArtClass);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4)) & 0xC0003F) == 0)
        {
            _ = SacredEquipmentClassInference.Apply(result, data.Classes, data.ParentResource, data.ResourceName);
            flags = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
            if ((flags & 0xC0003F) == 0) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), flags | 0xC0003F);
        }
        result[25] = unchecked((byte)level.Level);
        _ = SacredEquipmentBonusNormalization.Apply(result, category, tuning.Bonuses, tuning.Normalization, tuning.Sockets);
        var singleBonus = tuning.Sockets.Budget(quality) == 0 ? false :
            (data.SingleMagicBonusItem ?? throw new InvalidOperationException("Original magic resource predicate unavailable."))(basis);
        SacredEquipmentMagicGeneration.Apply(result, category, data.Templates.Equipment[basis].UsageIdentifier,
            unchecked((uint)modifier.Definition.WeaponType), quality, difficulty,
            singleBonus, tuning, random, data.CombatArtClass);
        GeneratedName = Naming?.GenerateOrdinary(result, type, random);
        flags = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4)) | 0x10000000 | ((difficulty + 1) << 24);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), flags);
        var price = SacredEquipmentPricing.CalculateOrdinary(result, data.PriceContext(basis), tuning.Pricing,
            tuning.Levels, tuning.Bonuses, tuning.Normalization, tuning.Sockets);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), price);
        return result;
    }
}
