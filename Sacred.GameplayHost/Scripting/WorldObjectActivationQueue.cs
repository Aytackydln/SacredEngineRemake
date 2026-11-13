using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x640640 initial object activation, called by
/// 0x5FAFE0 BEFORE factory reference/owner assignment. The real empty-equipment
/// 0x54D6E0/0x450960/0x550470 callbacks perform no model work. This owns the
/// pending activation record; executing future node ticks remains separate.</summary>
public sealed class WorldObjectActivationQueue(uint initialActivationCount = 0)
{
    private readonly List<WorldObjectNode> _nodes = [];
    private readonly List<WorldObjectActivationRecord> _records = [];
    public uint ActivationCount { get; private set; } = initialActivationCount;
    public IReadOnlyList<WorldObjectNode> Nodes => _nodes.AsReadOnly();
    public IReadOnlyList<WorldObjectActivationRecord> Records => _records.AsReadOnly();

    public bool ActivateInitial(WorldCreatureInitialActor actor, double clock)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var node = actor.Node;
        if (node.Active) return false;
        if (!double.IsFinite(clock) || clock < 0 || actor.Base.ObjectReference != 0 || actor.Base.Owner != 0 ||
            (actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 || actor.EquipmentReferences.Any(reference => reference != 0))
            throw new NotSupportedException("Initial activation requires an unassigned, unloaded creature with empty equipment and a finite nonnegative clock.");
        return ActivateInitial(node, clock);
    }

    /// <summary>Common scheduler insertion after the caller has registered its
    /// constructed, unassigned object. Node flags and period come from its class.</summary>
    public bool ActivateInitial(WorldObjectNode node, double clock)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Active) return false;
        if (!double.IsFinite(clock) || clock < 0 || node.QueueKind != 1)
            throw new NotSupportedException("Initial activation requires queue one and a finite nonnegative scheduler clock.");
        ActivationCount = unchecked(ActivationCount + 1);
        node.Flags |= 0x80;
        node.StartedAt = clock;
        node.NextDue = SacredNodeActivationTime.Next(clock, node.PeriodMilliseconds);
        _nodes.Add(node);
        _records.Add(new(node));
        return true;
    }

    /// <summary>Gold 0x640620/0x6410A0 and ordinary empty callbacks. Used by
    /// sector deactivation after placement, before later caller initialization.</summary>
    public bool DeactivateInitial(WorldCreatureInitialActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Node.Active) return false;
        if ((actor.Base.Flags & GoldPhysicalStateLayout.ModelLoadedFlag) != 0 ||
            actor.EquipmentReferences.Any(reference => reference != 0) || actor.KiMode != 0 || actor.EiMode != 0 ||
            (actor.BehaviorFlags & 0x100) != 0)
            throw new NotSupportedException("Initial deactivation requires the ordinary unloaded empty-equipment callbacks.");
        var record = _records.FirstOrDefault(value => ReferenceEquals(value.Node, actor.Node));
        if (record is null) return false;
        record.Flags |= 1;
        actor.Node.Flags &= 0x7f;
        record.Node = null;
        _nodes.Remove(actor.Node);
        return true;
    }

    /// <summary>Common empty-callback node removal used by the compared fixed
    /// Effect direct-placement path. Particle-system activity is separate.</summary>
    public bool DeactivateInitial(WorldObjectNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var record = _records.FirstOrDefault(value => ReferenceEquals(value.Node,node));
        if (!node.Active || record is null) return false;
        record.Flags |= 1; node.Flags &= 0x7f; record.Node = null; _nodes.Remove(node);
        return true;
    }
}
