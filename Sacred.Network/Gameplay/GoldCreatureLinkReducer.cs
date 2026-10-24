namespace Sacred.Network.Gameplay;

/// <summary>Unconditional link fields and known roster updates; level/model/equipment effects remain command work.</summary>
internal static class GoldCreatureLinkReducer
{
    public static void Apply(GoldCreatureLinkEvent entry, Dictionary<uint, GoldReplicatedObject> objects)
    {
        var parent = objects[entry.ParentReference]; var child = objects[entry.ChildReference]; var state = child.Creature!;
        if (entry.AddsLink)
        {
            if (entry.ParentReference > 16)
                state = state.WithControlWord(GoldCreatureStateLayout.Word1F0Offset, parent.Creature!.Word1F0)
                    .WithControlWord(GoldCreatureStateLayout.Word1F4Offset, 1)
                    .WithControlWord(GoldCreatureStateLayout.Word249Offset, parent.Creature.Word249);
            else
            {
                state = state.WithControlWord(GoldCreatureStateLayout.Word251Offset, entry.ParentReference)
                    .WithControlWord(GoldCreatureStateLayout.Word1F4Offset, (state.Word1F4 & 0xEFF62000) | 0x100);
                if (state.Word1F0 == 6) state = state.WithControlWord(GoldCreatureStateLayout.Word1F0Offset, 3);
            }
        }
        else state = state.WithControlWord(GoldCreatureStateLayout.Word251Offset, uint.MaxValue)
            .WithControlWord(GoldCreatureStateLayout.Word1F4Offset, state.Word1F4 & 0xDFFFFEFB);
        objects[entry.ChildReference] = child with { Creature = state };
        // Reload for self-links: both endpoints can be the same actual native object.
        parent = objects[entry.ParentReference];
        objects[entry.ParentReference] = parent with { LinkedCreatures = parent.LinkedCreatures?.Apply(entry.ChildReference, entry.AddsLink,
            entry.ParentReference <= 16 && entry.Option) };
    }
}
