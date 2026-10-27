using System.Runtime.InteropServices;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Item names shared by the simulator and future source callers.</summary>
public sealed class WorldEquipmentNames
{
    private readonly SacredEquipmentNameGeneration _names;
    private readonly SacredEquipmentGenerationData _data;
    private readonly HashSet<uint> _setItems;
    public WorldEquipmentNames(SacredEquipmentGenerationData data)
    {
        _data = data;
        _setItems = data.Sets.Where(set => !set.IsEmpty).SelectMany(set => set.ItemIds).ToHashSet();
        var naming = data.Naming ?? throw new InvalidOperationException("Original naming data unavailable.");
        _names = new(new(naming.Rules, data.Classes, naming.Resources, data.Tuning.Bonuses, data.Tuning.Levels.LevelFactorHundred,
            type => data.Templates.Equipment.TryGetValue(type, out var shared) ? shared : null,
            type => data.Templates.GetItem(type).ModelDesc.Category,
            type => data.Templates.Items.TryGetValue(type, out var item) ? item.ModelDesc.ResourceId : 0,
            type => data.Modifiers.Find(type)?.Definition.WeaponType, data.ResourceName));
    }
    public SacredEquipmentNameResult Existing(uint type, uint nativeReference = 0) => _names.Existing(type, nativeReference);
    /// <summary>Original 0x5D5697 ordinary-generation call. A generated item
    /// can reuse a catalogue set visual without becoming an authored set piece.</summary>
    public SacredEquipmentNameResult GenerateOrdinary(Span<byte> values, uint visualType, SacredRandomSequence random)
        => _names.Generate(values, visualType, random);
    public SacredEquipmentNameResult Generate(Span<byte> values, uint visualType, SacredRandomSequence random)
    {
        if (values.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected 130-byte item values.", nameof(values));
        var instance = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(values);
        // Identity comes from the concrete rarity and original set membership.
        // An ordinary roll can reuse a unique template's visual resource.
        if ((instance.Flags & 0xF0000) == 0xF0000 || _setItems.Contains(visualType) ||
            _data.Templates.Equipment.TryGetValue(visualType, out var shared) && shared.SetType != 0)
            return Existing(visualType);
        return _names.Generate(values, visualType, random);
    }
}
