using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Item names shared by the simulator and future source callers.</summary>
public sealed class WorldEquipmentNames
{
    private readonly SacredEquipmentNameGeneration _names;
    public WorldEquipmentNames(SacredEquipmentGenerationData data)
    {
        var naming = data.Naming ?? throw new InvalidOperationException("Original naming data unavailable.");
        _names = new(new(naming.Rules, data.Classes, naming.Resources, data.Tuning.Bonuses, data.Tuning.Levels.LevelFactorHundred,
            type => data.Templates.Equipment.TryGetValue(type, out var shared) ? shared : null,
            type => data.Templates.GetItem(type).ModelDesc.Category,
            type => data.Templates.Items.TryGetValue(type, out var item) ? item.ModelDesc.ResourceId : 0,
            type => data.Modifiers.Find(type)?.Definition.WeaponType, data.ResourceName));
    }
    public SacredEquipmentNameResult Existing(uint type, uint nativeReference = 0) => _names.Existing(type, nativeReference);
    public SacredEquipmentNameResult Generate(Span<byte> values, uint visualType, SacredRandomSequence random) => _names.Generate(values, visualType, random);
}
