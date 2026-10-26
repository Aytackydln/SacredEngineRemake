using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.GameBin.EquipmentVariants;
using Sacred.Assets.GameBin.WeaponModifiers;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.EquipmentVariants;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Equipment;

/// <summary>Original equipment files and extracted executable services. Runtime
/// generated/shared values belong to the gameplay host, not this immutable input.</summary>
public sealed record SacredEquipmentGenerationData(SacredObjectTemplateArchive Templates,
    SacredEquipmentModifierResolver Modifiers, SacredEquipmentGenerationTuning Tuning,
    IReadOnlyList<IReadOnlyList<SacredEquipmentVariantLayout>> Variants,
    SacredEquipmentClassRules Classes, SacredEquipmentClassGateRules ClassGate,
    SacredEquipmentSelectionPoolRules Pools, Func<uint, byte> CombatArtClass,
    Func<uint, string?> ResourceName, Func<uint, SacredEquipmentPriceOverrides> PriceOverrides,
    SacredItemCategoryRules? CategoryRules = null, Func<uint, bool>? SingleMagicBonusItem = null)
{
    public SacredEquipmentNamingData? Naming { get; init; }
    public uint? ParentResource(uint type) => Templates.Equipment.TryGetValue(type, out var definition) ? definition.BaseItemId : null;

    public SacredEquipmentPriceContext PriceContext(uint type)
    {
        var definition = Templates.Equipment[type];
        var modifier = Modifiers.Find(type);
        var overrides = PriceOverrides(type);
        return new(Templates.GetItem(type).ModelDesc.Category, modifier is not null, modifier?.Definition.Modifier ?? 0,
            definition.SetType, overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
    }

    public static SacredEquipmentGenerationData Load(string installation)
    {
        var templates = SacredObjectTemplateArchive.Load(installation);
        var modifiers = WeaponModifiersBinArchive.Load(Path.Combine(installation, "bin", "wpmod.bin"));
        var tuning = EquipmentGenerationTuningBinReader.Read(File.ReadAllBytes(Path.Combine(installation, "bin", "Balance.bin")));
        var variants = EquipmentVariantsBinArchive.Load(Path.Combine(installation, "bin", "Rust.bin"));
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, filename);
            if (!File.Exists(path)) continue;
            try
            {
                var image = File.ReadAllBytes(path);
                var selection = SacredEquipmentSelectionDefinitions.Read(image);
                var classes = SacredEquipmentClassDefinitions.Read(image);
                var gate = SacredEquipmentSelectionClassGateDefinitions.Read(image);
                var generation = SacredEquipmentGenerationDefinitions.Read(image);
                var names = SacredItemResourceNames.Read(image);
                var prices = SacredEquipmentPriceDefinitions.Read(image);
                var associations = new SacredEquipmentModifierResolver(modifiers,
                    templates.Equipment.ToDictionary(pair => pair.Key, pair => pair.Value.BaseItemId), selection.ModifierRedirects);
                return new(templates, associations, tuning, variants, classes, gate, selection.PoolRules,
                    generation.CombatArtClass, type => names.Find(type) ?? ModelName(templates, type), prices.Overrides,
                    SacredItemCategoryDefinitions.Read(image), SacredEquipmentMagicDefinitions.Read(image))
                {
                    Naming = new(SacredEquipmentNameDefinitions.Read(image), new(SacredResUnpack.UnpackAsDictionary(
                        Path.Combine(installation, "scripts", "us", "global.res"))))
                };
            }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment generation definitions unavailable.", failure);
    }

    private static string? ModelName(SacredObjectTemplateArchive templates, uint type)
    {
        if (!templates.Items.TryGetValue(type, out var item)) return null;
        return item.ModelName.Contains(".grn", StringComparison.Ordinal) || item.ModelName.Contains(".GRN", StringComparison.Ordinal) ? item.ModelName : null;
    }
}
