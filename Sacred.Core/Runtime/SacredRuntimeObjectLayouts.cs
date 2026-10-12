using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Core.Runtime;

/// <summary>Three 32-bit addresses, independent of the remake process's pointer size.
/// These describe native memory; they must never be dereferenced as managed pointers.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 12)]
public readonly struct SacredRuntimeVectorLayout
{
    [FieldOffset(0)] public readonly uint Begin;
    [FieldOffset(4)] public readonly uint End;
    [FieldOffset(8)] public readonly uint CapacityEnd;
}

[InlineArray(19)] public struct SacredEquipmentReferences { private uint _first; }
[InlineArray(6)] public struct SacredActorHitPointWords { private int _first; }

/// <summary>Verified 2004 cCreature relationships; rendering, AI and movement bytes
/// remain unmapped here. Gold object offsets require separate verification.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredActorObjectLayout
{
    public const int Size = 1264;
    [FieldOffset(0x1B0)] public readonly SacredEquipmentReferences Equipment;
    [FieldOffset(0x208)] public readonly uint InventoryId;
    [FieldOffset(0x36C)] public readonly SacredActorComponentLayout Character;
}

/// <summary>2004 cCreatureHero: values and variable-length art/combo/extra-bonus state.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredActorComponentLayout
{
    public const int Size = 328;
    [FieldOffset(0), BinaryUnknown] public readonly uint VTable;
    [FieldOffset(4)] public readonly uint ParentAddress;
    [FieldOffset(8)] public readonly SacredActorValuesLayout Values;
    [FieldOffset(0x102)] public readonly SacredRuntimeVectorLayout CombatArts;
    [FieldOffset(0x10E)] public readonly SacredRuntimeVectorLayout Combos;
    [FieldOffset(0x11A)] public readonly SacredRuntimeVectorLayout ExtraBonuses;
    [FieldOffset(0x126), BinaryUnknown] public readonly byte Entry;
    [FieldOffset(0x127)] public readonly byte UpdateNetwork;
    /// <summary>Native cCreatureHero::TP[6], signed runtime words outside sCharValHero.
    /// HUD rendering reads TP[2] / TP[1]; other entries remain uninterpreted.</summary>
    [FieldOffset(0x128)] public readonly SacredActorHitPointWords HitPointWords;
    /// <summary>TP[1], written by calcHitpoints; denominator of the portrait health fraction.</summary>
    [FieldOffset(0x12C)] public readonly int MaximumHitPoints;
    /// <summary>TP[2], current HP; numerator of the portrait health fraction.</summary>
    [FieldOffset(0x130)] public readonly int CurrentHitPoints;
}

/// <summary>2004 cItemBase relationships. MergedValues contains sWeaponInfo records;
/// TotalValues is the cached resolved block, not an archive definition.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredItemObjectLayout
{
    public const int Size = 496;
    [FieldOffset(0x108)] public readonly uint GroupResource;
    [FieldOffset(0x10C)] public readonly SacredRuntimeVectorLayout MergedValues;
    [FieldOffset(0x124)] public readonly SacredEquipmentInstanceLayout TotalValues;
    [FieldOffset(0x1EC)] public readonly uint InventoryOwnerReference;
}
