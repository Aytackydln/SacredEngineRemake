namespace Sacred.Network.Gameplay;

/// <summary>Native +0x570 pairing and flag copies only; actor/equipment refresh at 0x43DD70 remains separate.</summary>
internal static class GoldCreatureMountReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldCreatureMountEvent entry, Func<uint, bool> isHorseResource)
    {
        var changed = false;
        ApplyEndpoint(entry.FirstReference, entry.SecondReference);
        ApplyEndpoint(entry.SecondReference, entry.FirstReference);
        return changed;

        void ApplyEndpoint(uint reference, uint partner)
        {
            if (objects.TryGetValue(reference, out var actor) && actor.Creature is not null && (actor.BaseState.Flags & 0x10) == 0)
                SetPartner(reference, partner);
        }

        void SetPartner(uint reference, uint partner)
        {
            var actor = objects[reference];
            var previous = actor.MountPartnerReference ?? 0;
            if (previous != 0 && previous != partner && Horse(previous) is not null)
            {
                // The original recursive zero-link call does not require the previous horse to be active.
                SetPartner(previous, 0);
                var detached = objects[previous];
                Store(previous, detached with { BaseState = detached.BaseState with { Flags = detached.BaseState.Flags | GoldCreatureMountLayout.DetachSetMask } });
                // Native code also refreshes the old horse when this creature's first equipment slot is populated.
                // That appearance/equipment side effect is deferred; serialized slots are retained.
            }
            actor = objects[reference];
            if (partner == 0)
            {
                Store(reference, actor with { MountPartnerReference = 0 });
                return;
            }
            var horse = Horse(partner);
            if (horse is null) return; // Native invalid target leaves the caller's old link word unchanged.
            if (actor.MountPartnerReference == partner && horse.MountPartnerReference == reference) return;
            Store(reference, actor with { MountPartnerReference = partner });
            horse = objects[partner];
            var flags = reference <= 16 ? horse.BaseState.Flags & ~GoldCreatureMountLayout.PlayerLinkClearMask : horse.BaseState.Flags;
            Store(partner, horse with { MountPartnerReference = reference, BaseState = horse.BaseState with { Flags = flags } });
        }

        GoldReplicatedObject? Horse(uint reference) => objects.TryGetValue(reference, out var actor) &&
            actor.Creature is not null && isHorseResource(actor.BaseState.ObjectType) ? actor : null;

        void Store(uint reference, GoldReplicatedObject next)
        {
            if (objects[reference] == next) return;
            objects[reference] = next;
            changed = true;
        }
    }
}
