namespace Sacred.Network.Gameplay;

/// <summary>Shared logical removal and owner effects. Removal retains an inactive record like the native manager;
/// model retirement/fade timing is handled by presentation.</summary>
internal static class GoldObjectLifecycleReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldGameEvent entry)
    {
        var reference = entry switch
        {
            GoldObjectRemovalEvent removal => removal.ObjectReference,
            GoldObjectOwnerEvent owner => owner.ObjectReference,
            _ => throw new ArgumentException("Expected a Gold object lifecycle event.", nameof(entry))
        };
        if (!objects.TryGetValue(reference, out var current) || (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        var changed = entry switch
        {
            GoldObjectRemovalEvent removal => current with
            {
                BaseState = current.BaseState with { Flags = current.BaseState.Flags | GoldObjectLifecycleLayout.InactiveFlag },
                Movement = null,
                Attack = null, AttackVersion = current.AttackVersion + 1,
                Flight = null, FlightVersion = current.FlightVersion + 1,
                RemovalOpcode = removal.Message.Opcode
            },
            GoldObjectOwnerEvent owner => current with { BaseState = current.BaseState with { Owner = owner.Owner } },
            _ => current
        };
        if (changed == current) return false;
        objects[reference] = changed;
        return true;
    }
}
