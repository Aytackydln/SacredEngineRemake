using Sacred.Inventory.Actors;

namespace Sacred.Engine.Assets;

/// <summary>Mutable equipment state for the selected playable character.</summary>
internal sealed class PlayerCharacterLoadout(
    uint entryId,
    TestCharacterDefinition definition,
    SacredGameActor actor)
{
    public uint EntryId { get; } = entryId;
    public TestCharacterDefinition Definition { get; } = definition;
    public SacredGameActor Actor { get; } = actor;

    public PlayerCharacterLoadout Snapshot() => new(EntryId, Definition, Actor.Clone());
}
