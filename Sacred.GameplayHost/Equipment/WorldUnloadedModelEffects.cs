using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Concrete original model effects while model-loaded flags are clear.
/// Node activation does not set that flag. Loaded sequences require the separate
/// Granny deactivation/binding services and are rejected before mutation.</summary>
public static class WorldUnloadedModelEffects
{
    /// <summary>Initial 0x555E00 attachment preparation: 0x44D4F0 clears +7C
    /// and calls GrannySetSequencePosition with zero handles (error 6, ignored),
    /// then 0x44DD90/0x44DBF0 sets direction to (1,0,0). The original DLL result
    /// is independently measured; no loaded sequence exists to update.</summary>
    public static void PrepareInitialAttachment(WorldEquipmentObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Physical) throw new ArgumentException("Attachment preparation requires a physical item.", nameof(item));
        RequireUnloaded(item.Base.Flags);
        item.Vector7C = default;
        item.Direction = new(1, 0, 0);
    }

    /// <summary>Gold 0x44D7B0: no loaded sequence to deactivate; ##Root lookup
    /// 0x44CA30 fails without a sequence. 0x44DBF0 preserves direction and does
    /// no model work. Clear the two native vectors and mark placement dirty.</summary>
    public static void Reset(WorldEquipmentObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Physical) throw new ArgumentException("Physical model reset requires a physical object.", nameof(item));
        RequireUnloaded(item.Base.Flags);
        item.Vector7C = default;
        item.Vector88 = default;
        item.Base = item.Base with { Flags = item.Base.Flags | 0x200 };
    }

    /// <summary>Gold 0x555300, actor model absent: resolve the physical item,
    /// execute its actual reset path, clear placement-dirty and return the
    /// unchanged slot. No binding, normalization or result callback runs here.</summary>
    public static ushort Attach(WorldCreatureEquipmentState actor, ushort slot, Func<uint, WorldEquipmentObject?> find)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(find);
        if (slot >= 19) throw new ArgumentOutOfRangeException(nameof(slot), "Expected an ordinary native equipment slot.");
        RequireUnloaded(actor.Base.Flags);
        if (find(actor[slot]) is not { Physical: true } item) return slot;
        Reset(item);
        item.Base = item.Base with { Flags = item.Base.Flags & ~0x200u };
        return slot;
    }

    private static void RequireUnloaded(uint flags)
    {
        if ((flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0)
            throw new NotSupportedException("A loaded sequence requires actual Granny model effects.");
    }
}
