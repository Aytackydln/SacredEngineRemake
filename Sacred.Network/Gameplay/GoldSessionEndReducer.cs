namespace Sacred.Network.Gameplay;

internal static class GoldSessionEndReducer
{
    /// <summary>Native 7ECE50 retires active objects owned by other clients before transport teardown.</summary>
    public static void Apply(Dictionary<uint, GoldReplicatedObject> objects, byte localSlot,
        GoldBowProjectileReplica projectiles, GoldSessionNoticeEvent notice)
    {
        foreach (var obj in objects.Values.ToArray())
        {
            if (obj.BaseState.Owner == localSlot || (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            projectiles.Remove(obj.BaseState.ObjectReference);
            var message = notice.Message with { Opcode = GoldObjectLifecycleLayout.ImmediateRemoveOpcode,
                Payload = BitConverter.GetBytes(obj.BaseState.ObjectReference) };
            GoldObjectLifecycleReducer.Apply(objects, new GoldObjectRemovalEvent(message, obj.BaseState.ObjectReference));
        }
    }
}
