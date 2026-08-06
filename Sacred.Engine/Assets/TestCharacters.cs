using System;
using System.Collections.Generic;
using Sacred.Inventory.Actors;

namespace Sacred.Engine.Assets;

/// <summary>Known game-file examples used solely by the model-cycling developer scene.</summary>
internal static class TestCharacters
{
    // Base character item ids (Items.pak ItemId, not descriptor rows).
    private const uint Seraphim = 1;
    private const uint Gladiator = 2;
    private const uint BattleMage = 3;
    private const uint DarkElf = 4;
    private const uint Vampiress = 6;
    private const uint Dwarf = 8;
    private const uint Daemon = 9;
    private const uint WoodElf = 108;

    // Equipment item ids. Keeping names here makes these intentional test fixtures, not engine rules.
    private const uint DaemonHelm = 1222, DarkElfBreastplate = 1251, SeraphimHelm = 1840;
    private const uint SeraphimGodsShield = 4017, BattleMageCowl = 3219, SeraphimWings = 4006;

    private const uint SeraphimHair = 4007,
        SeraphimArms = 4082,
        SeraphimBoots = 4083,
        SeraphimBelt = 4079,
        SeraphimShoulder = 4084;

    private const uint VampiressDayHair = 4028, VampiressNightHair = 4029, GladiatorBelt = 4054;

    private const uint BattleMageTurban = 1315, BattleMagePad = 1424;

    private const uint SeraphimSword = 1851,
        ElvenBow = 1747,
        VampireSword = 1771,
        BattleMageStaff = 1877,
        BattleMageShortStaff = 1876,
        GladSword = 1725,
        DelfPoisonBlade = 1862,
        DelfFireBlade = 1864,
        LargeTorch = 5633;

    private static readonly Dictionary<ItemSlot, uint> SeraphimItems = new()
    {
        [ItemSlot.RightHand] = SeraphimSword,
        [ItemSlot.Wings] = SeraphimWings,
        [ItemSlot.Head] = SeraphimHelm,
        [ItemSlot.LeftHand] = SeraphimGodsShield,
        [ItemSlot.Arms] = SeraphimArms,
        [ItemSlot.Feet] = SeraphimBoots,
        [ItemSlot.Belt] = SeraphimBelt,
        [ItemSlot.Shoulder] = SeraphimShoulder,
    };
    private static readonly Dictionary<ItemSlot, uint> GladiatorItems = new()
    {
        [ItemSlot.Belt] = GladiatorBelt,
        [ItemSlot.RightHand] = GladSword,
    };
    private static readonly Dictionary<ItemSlot, uint> WoodElfItems = new()
    {
        [ItemSlot.LeftHand] = ElvenBow,
    };
    private static readonly Dictionary<ItemSlot, uint> DarkElfItems = new()
    {
        [ItemSlot.Body] = DarkElfBreastplate,
        [ItemSlot.RightHand] = DelfPoisonBlade,
        [ItemSlot.LeftHand] = DelfFireBlade,
    };
    private static readonly Dictionary<ItemSlot, uint> BattleMageITems = new()
    {
        [ItemSlot.Head] = BattleMageCowl,
        [ItemSlot.LeftHand] = BattleMageStaff,
    };
    private static readonly Dictionary<ItemSlot, uint> BattleMageITems2 = new()
    {
        [ItemSlot.Head] = BattleMageTurban,
        [ItemSlot.Body] = BattleMagePad,
        [ItemSlot.LeftHand] = BattleMageShortStaff,
    };
    private static Dictionary<ItemSlot, uint> VampiressNItems = new()
    {
        [ItemSlot.Head] = VampiressNightHair,
    };
    private static readonly Dictionary<ItemSlot, uint> VampiressDItems = new()
    {
        [ItemSlot.Body] = 3271,
        [ItemSlot.Arms] = 3272,
        [ItemSlot.Hands] = 3273,
        [ItemSlot.Legs] = 3274,
        [ItemSlot.RightHand] = VampireSword,
    };
    private static readonly Dictionary<ItemSlot, uint> DwarfItems = new();
    private static readonly Dictionary<ItemSlot, uint> DaemonItems = new()
    {
        [ItemSlot.Head] = DaemonHelm,
        [ItemSlot.RightHand] = LargeTorch,
    };

    public static IReadOnlyList<TestCharacterDefinition> All { get; } =
    [
        new(Seraphim, "SERAPHIM.GRN", "Seraphim", SeraphimItems),
        new(Gladiator, "GLADIATOR.GRN", "Gladiator", GladiatorItems),
        new(WoodElf, "Waldelfe.grn", "Wood Elf", WoodElfItems),
        new(DarkElf, "DARKELVE.GRN", "Dark Elf", DarkElfItems),
        new(BattleMage, "MAGICIAN.GRN", "Battle Mage", BattleMageITems),
        new(BattleMage, "MAGICIAN.GRN", "Battle Mage 2", BattleMageITems2),
        new(Vampiress, "VLADY_D.GRN", "Vampiress D", VampiressDItems),
        new(Vampiress, "VLADY_N.GRN", "Vampiress N", VampiressNItems),
        new(Dwarf, "dwarf.grn", "Dwarf", DwarfItems),
        new(Daemon, "Daemonia.grn", "Daemon", DaemonItems)
    ];

    public static uint ResolveEntryId(string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            for (var index = 0; index < All.Count; index++)
            {
                if (string.Equals(All[index].DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return checked((uint)index + 1);
            }
        }

        return 1;
    }

    public static string GetDisplayName(uint entryId)
    {
        var index = checked((int)entryId - 1);
        return (uint)index < (uint)All.Count
            ? All[index].DisplayName
            : All[0].DisplayName;
    }
}

internal readonly record struct TestCharacterDefinition(
    uint BaseItemId,
    string ModelName,
    string DisplayName,
    IReadOnlyDictionary<ItemSlot, uint> Items);
