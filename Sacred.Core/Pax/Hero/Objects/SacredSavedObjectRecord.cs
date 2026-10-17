namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>One C2/C8 slot; a zero resource is a four-byte empty record.</summary>
public sealed record SacredSavedObjectRecord(uint Resource, byte Category, SacredSavedItem? Item, SacredSavedCreature? Creature)
{
    public bool IsEmpty => Resource == 0;
    public SacredSavedObjectBaseLayout BaseState => Item?.BaseState ?? Creature?.BaseState ?? default;
    public int SerializedSize => 4 + (Item?.SerializedSize ?? Creature?.SerializedSize ?? 0);
}
