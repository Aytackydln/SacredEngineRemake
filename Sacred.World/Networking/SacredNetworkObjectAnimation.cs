using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Shared current activation pose for received world props and headless observers.</summary>
public static class SacredNetworkObjectAnimation
{
    public static bool IsActivated(GoldReplicatedObject obj, GoldWorldReplica world)
    {
        if (obj.Container is not null && (obj.BaseState.Flags & GoldContainerActivationCodec.ActivatedFlag) != 0)
        {
            // Native physical initialization restores 0xAA from the container's opened bit
            // (0x4509C5); GameServer's headless stream can still carry motion zero.
            // Deferred callbacks are not completed just because receive set its one-shot guard.
            if (world.ContainerInteractions.Pending.Any(work =>
                    work.Event.ObjectReference == obj.BaseState.ObjectReference && work.Object.InstanceVersion == obj.InstanceVersion))
                return obj.Physical?.Word64 == 0xaa;
            return obj.Physical?.Word64 != 0xab;
        }
        if (obj.BaseState.TriggerReference != 0 &&
            world.WorldTriggers.Records.TryGetValue(obj.BaseState.TriggerReference, out var trigger))
        {
            if (obj.ActivationTriggerState == trigger.State && obj.Physical?.Word64 is 0xaa or 0xab)
                return obj.Physical.Word64 == 0xaa;
            return (trigger.State & 1) != 0;
        }
        return obj.Physical?.Word64 == 0xaa;
    }
}
