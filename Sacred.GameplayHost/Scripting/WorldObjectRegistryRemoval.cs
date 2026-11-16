namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared low-level slot destruction and the separate high-level
/// local-reference protection gate. Full removal effects remain separate.</summary>
public static class WorldObjectRegistryRemoval
{
    /// <summary>Gold 0x5FB320. Clear before destructor reentry, retain logical
    /// length, do not repair references or filter flags. Entry zero is permitted
    /// by this low-level helper even though ordinary lookup reserves it.</summary>
    public static void DestroySlot<TObject>(uint reference, IList<TObject?> slots,
        Action<TObject> destroy) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(destroy);
        if (reference >= (uint)slots.Count) return;
        var value = slots[(int)reference];
        if (value is null) return;
        slots[(int)reference] = null;
        destroy(value);
    }

    /// <summary>Gold 0x5FBDB0..0x5FBE37. Raw controller bytes +1 and +0x28
    /// retain neutral names until their wider meaning is established. This gate
    /// runs before the zero-reference check and all object removal effects.</summary>
    public static bool ProtectsLocalReference(uint reference, uint localReference,
        bool multiplayer, bool controllerFlag1, bool connected, bool controllerFlag28) =>
        reference == localReference && !(multiplayer && controllerFlag1 && connected && !controllerFlag28);
}
