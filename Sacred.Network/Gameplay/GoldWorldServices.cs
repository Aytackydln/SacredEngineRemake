namespace Sacred.Network.Gameplay;

/// <summary>Composes replica work sources and clears session services without discarding their wiring.</summary>
internal static class GoldWorldServices
{
    public static void Connect(GoldWorldReplica world)
    {
        world.Combat.Published += work => world.Consumers.Publish("combat", work.Event.Message, work);
        world.CreatureActions.Published += work => world.Consumers.Publish("actions", work.Event.Message, work);
        world.AttachedFx.Published += work => world.Consumers.Publish("fx", work.Event.Message, work);
        world.CreatureCommands.Interactions.Published += work => world.Consumers.Publish(work.FxCreation is null ? "actions" : "fx", work.Event.Message, work);
        world.ReceiveWork.FxPublished += (source, work) => world.Consumers.Publish("fx", source.Message, work);
        world.ObjectPresentation.Published += work => world.Consumers.Publish("actions", work.Event.Message, work);
        world.Session.Chat.Published += work => world.Consumers.Publish("chat", work.Message, work);
        world.Session.ChannelChat.Published += work => world.Consumers.Publish("chat", work.Event.Message, work);
        world.WorldBanks.Counters.Messages.Published += (work, source) => world.Consumers.Publish("hud", source, work);
        world.Session.RegionCountdowns.Published += (work, source) => world.Consumers.Publish("hud", source, work);
    }

    public static void Clear(GoldWorldReplica world)
    {
        world.Consumers.Clear(); world.Combat.Clear(); world.CreatureControls.Clear();
        world.BowProjectiles.Clear(); world.AttachedFx.Clear(); world.ObjectPresentation.Clear();
        world.SpecialFx.Clear(); world.CreatureActions.Clear(); world.CreatureCommands.Clear(); world.ReceiveWork.Clear();
        world.SectorInterest.Clear(); world.SectorWork.Clear(); world.DeathScripts.Clear(); world.Statistics.Clear();
        world.WorldBanks.Clear(); world.Session.Clear(); world.Regions.Clear(); world.ScriptVariables.Clear();
        world.ScriptResources.Clear(); world.ScriptDialogs.Clear(); world.NpcIdleAnimations.Clear();
        world.DoorInteractions.Clear(); world.ContainerInteractions.Clear(); world.WorldTriggers.Clear();
        world.MovementTriggers.Clear(); world.TriggerReferences.Clear(); world.Calendar.Clear();
        world.Weather.Clear(); world.OwnedCreatureThinking.Clear();
    }
}
