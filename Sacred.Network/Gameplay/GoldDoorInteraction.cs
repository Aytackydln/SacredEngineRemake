namespace Sacred.Network.Gameplay;

/// <summary>One received activation awaiting cObject3D dispatch. Arrival-time instances are retained;
/// consumers must not redirect it to a later object using the same reference.</summary>
public sealed record GoldDoorInteraction(GoldDoorEvent Event, GoldReplicatedObject Object,
    GoldWorldTriggerState? Trigger)
{
    public uint Action => GoldDoorLayout.ActivationAction;
    public uint TriggerReference => Object.BaseState.TriggerReference;
}
