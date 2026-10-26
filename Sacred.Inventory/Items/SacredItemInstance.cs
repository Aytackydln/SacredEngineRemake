using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Bonuses;
using Sacred.Core.Pak.Weapon.Names;

namespace Sacred.Inventory.Items;

/// <summary>One concrete item. Resolved numbers never change when its shared definition or owner changes.
/// Level is identity data: this class does not run drop generation or transpose authored numbers.</summary>
public sealed class SacredItemInstance
{
    public SacredItemInstance(SacredEquipment definition, ushort level, SacredEquipmentBaseStats baseStats,
        SacredEquipmentDamage damage, IEnumerable<SacredEquipmentBonus> bonuses)
    {
        if (level > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(level), "The native item level is a byte.");
        InstanceId = Guid.NewGuid();
        Definition = definition;
        Level = level;
        BaseStats = baseStats;
        Damage = damage;
        Bonuses = Array.AsReadOnly(bonuses.ToArray());
        Requirements = definition.Requirements;
        Price = definition.Price;
        SlotTypes = definition.SlotTypes;
        BlacksmithLevel = definition.BlacksmithLevel;
    }

    public Guid InstanceId { get; }
    public SacredEquipment Definition { get; }
    public ushort Level { get; }
    public SacredEquipmentBaseStats BaseStats { get; }
    public SacredEquipmentDamage Damage { get; }
    public IReadOnlyList<SacredEquipmentBonus> Bonuses { get; }
    public SacredEquipmentNameResult? ResolvedName { get; private set; }
    public void SetResolvedName(SacredEquipmentNameResult name) => ResolvedName = name;
    public SacredEquipmentRequirements Requirements { get; private init; }
    public uint Price { get; private init; }
    public SacredEquipmentSlotTypes SlotTypes { get; private init; }
    public byte BlacksmithLevel { get; private init; }
    public uint? ResolvedFlags { get; private init; }
    public SacredEquipmentInstanceLayout? NativeValues { get; private init; }
    /// <summary>Lossless merged socket records, including records whose parent slot is inactive.</summary>
    public IReadOnlyList<SacredEquipmentInstanceLayout> SocketRecords { get; private init; } = Array.Empty<SacredEquipmentInstanceLayout>();

    /// <summary>A PoC instance of authored values, without invented random or level-scaled rolls.</summary>
    public static SacredItemInstance FromDefinition(SacredEquipment definition) =>
        new(definition, definition.SpawnLevel, definition.BaseStats, definition.Damage,
            SacredEquipmentBonusDecoder.Decode(definition));

    /// <summary>Imports an already resolved native value block. The caller supplies its matching definition.</summary>
    public static SacredItemInstance FromNativeRecord(SacredEquipment definition, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("An item value block must contain exactly 130 bytes.", nameof(bytes));
        var source = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes);
        if (source.ItemType != definition.IdemId)
            throw new ArgumentException("The native item value block does not match the equipment definition.", nameof(bytes));
        return FromNativeValues(definition, source);
    }

    /// <summary>Imports a concrete object's resolved values. Its current visual resource is independent
    /// of sWeaponInfo.ItemType, which can retain a different original resource.</summary>
    public static SacredItemInstance FromNativeObjectRecord(SacredEquipment definition, uint objectType, ReadOnlySpan<byte> bytes)
    {
        if (definition.IdemId != objectType)
            throw new ArgumentException("The equipment definition must match the object's visual resource.", nameof(definition));
        if (bytes.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("An item value block must contain exactly 130 bytes.", nameof(bytes));
        return FromNativeValues(definition, MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes));
    }

    public static SacredItemInstance FromNativeObjectRecords(SacredEquipment definition, uint objectType,
        ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> socketRecords)
    {
        if (definition.IdemId != objectType || bytes.Length != SacredEquipmentInstanceLayout.Size ||
            socketRecords.Length % SacredEquipmentInstanceLayout.Size != 0)
            throw new ArgumentException("Native item identity or record extent is invalid.");
        return FromNativeValues(definition, MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes), socketRecords);
    }

    private static SacredItemInstance FromNativeValues(SacredEquipment definition, SacredEquipmentInstanceLayout source,
        ReadOnlySpan<byte> socketRecords = default)
    {
        var bonuses = new List<SacredEquipmentBonus>();
        AddBonuses(source);
        var sockets = new SacredEquipmentInstanceLayout[socketRecords.Length / SacredEquipmentInstanceLayout.Size];
        for (var index = 0; index < sockets.Length; index++)
        {
            sockets[index] = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(socketRecords[(index * SacredEquipmentInstanceLayout.Size)..]);
            // Gold cItemBase::getSlotType (0x5BE9E0): only populated socket kinds contribute.
            if (index < 8 && source.SlotTypes[index] is >= 1 and <= 4) AddBonuses(sockets[index]);
        }
        return new(definition, source.Level,
            new(source.Attack, source.Defense, source.BW, source.PhysicalProtection,
                source.FireProtection, source.MagicProtection, source.PoisonProtection), source.Damage, bonuses)
        {
            Requirements = new(source.MinimumLevel, source.MinimumStrength, source.MinimumDexterity,
                source.MinimumCharisma, source.MinimumEndurance, source.MinimumSkill, source.MinimumSkillLevel),
            Price = source.Price, SlotTypes = source.SlotTypes, BlacksmithLevel = source.BlacksmithLevel,
            ResolvedFlags = source.Flags, NativeValues = source, SocketRecords = Array.AsReadOnly(sockets)
        };

        void AddBonuses(SacredEquipmentInstanceLayout values)
        {
            for (var index = 0; index < 8; index++)
                if (values.BonusGroups[index] != 0 || values.BonusTypes[index] != 0)
                    bonuses.Add(SacredEquipmentBonusDecoder.Decode(values.BonusGroups[index], values.BonusTypes[index], values.BonusValues[index]));
        }
    }
}
