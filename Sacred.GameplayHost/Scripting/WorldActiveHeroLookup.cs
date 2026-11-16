namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x603E30 / 0x5FE000 lookup rules. Slots must expose the native
/// logical length including holes and reserved entry zero, never capacity or
/// dictionary entry count. Identity lifetime and dynamic-cast provenance belong
/// to the caller. Reference repair is an observable mutation before casting.</summary>
public static class WorldActiveHeroLookup
{
    public static TObject? ResolveObject<TObject>(uint reference, IReadOnlyList<TObject?> slots,
        Func<TObject, uint> readReference, Action<TObject, uint> writeReference,
        Action<uint, uint> reportRepair) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(readReference);
        ArgumentNullException.ThrowIfNull(writeReference);
        ArgumentNullException.ThrowIfNull(reportRepair);
        if (reference == 0 || reference >= (uint)slots.Count) return null;
        var value = slots[(int)reference];
        if (value is null) return null;
        var stored = readReference(value);
        if (stored != reference)
        {
            writeReference(value, reference);
            reportRepair(reference, stored);
        }
        // Native reloads the slot after the diagnostic. Do not retain a stale
        // object across a caller-supplied diagnostic which changes that slot.
        return slots[(int)reference];
    }

    public static TCreature? Resolve<TObject, TCreature>(uint controllerReference,
        IReadOnlyList<TObject?> slots, Func<TObject, uint> readReference,
        Action<TObject, uint> writeReference, Action<uint, uint> reportRepair,
        Func<TObject?, TCreature?> castCreature) where TObject : class where TCreature : class
    {
        ArgumentNullException.ThrowIfNull(castCreature);
        // Zero bypasses RTTI; nonzero missing/out-of-range objects still reach
        // the native cast with null. Creature is not necessarily a Hero class.
        if (controllerReference == 0) return null;
        return castCreature(ResolveObject(controllerReference, slots, readReference, writeReference, reportRepair));
    }
}
