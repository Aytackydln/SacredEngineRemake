using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Sacred.Inventory.Actors;

/// <summary>Runtime companion references; an empty party has no fabricated members.</summary>
public sealed class SacredActorParty
{
    private readonly List<SacredGameActor> _members = [];
    public ReadOnlyCollection<SacredGameActor> Members { get; }
    public SacredActorParty() => Members = _members.AsReadOnly();
    public void Add(SacredGameActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_members.Contains(actor)) throw new ArgumentException("The actor is already in this party.", nameof(actor));
        _members.Add(actor);
    }
    public bool Remove(SacredGameActor actor) => _members.Remove(actor);
    public void Clear() => _members.Clear();
}
