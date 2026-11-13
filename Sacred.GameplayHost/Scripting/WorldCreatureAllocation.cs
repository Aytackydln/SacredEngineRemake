namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FAFE0's pre-reserved ordinary allocation path. Storage and
/// constructor are supplied; constructor implementation has separate native
/// coverage. The registry callback runs before initial activation, while base
/// reference/owner remain zero. Identity assignment follows in 0x5FB530.</summary>
public static class WorldCreatureAllocation
{
    public static WorldCreatureInitialActor? Allocate(uint reservedReference,
        WorldObjectAllocationCounter counter, WorldObjectActivationQueue activation,
        Func<uint, WorldCreatureInitialActor?> construct, Action<uint, WorldCreatureInitialActor> register, double clock)
    {
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(construct);
        ArgumentNullException.ThrowIfNull(register);
        if (reservedReference <= 16) throw new ArgumentOutOfRangeException(nameof(reservedReference));
        if (!double.IsFinite(clock) || clock < 0) throw new NotSupportedException("Initial allocation requires a finite nonnegative scheduler clock.");
        counter.BeginAllocation();
        var actor = construct(reservedReference);
        if (actor is null) return null;
        if (actor.Base.ObjectReference != 0 || actor.Base.Owner != 0 || actor.Node.Active)
            throw new InvalidOperationException("Constructor must return the initial unassigned inactive actor.");
        actor = actor with { Base = actor.Base with { Byte38 = counter.Value } };
        register(reservedReference, actor);
        activation.ActivateInitial(actor, clock);
        return actor;
    }
}
