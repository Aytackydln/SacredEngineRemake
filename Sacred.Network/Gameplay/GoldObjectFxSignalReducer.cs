namespace Sacred.Network.Gameplay;

internal static class GoldObjectFxSignalReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldObjectFxSignalEvent incoming)
    {
        var entry = GoldObjectFxSignalCodec.Decode(incoming.Message);
        // The native lookup casts to cObjectFX without an additional inactive-object guard.
        if (!objects.TryGetValue(entry.ObjectReference, out var obj) || obj.Fx is null) return false;
        var runtime = obj.SpecialFx ?? new GoldSpecialFxRuntime();
        objects[entry.ObjectReference] = obj with
        { SpecialFx = runtime with { ParticleFlags = entry.Notification, Version = runtime.Version + 1 } };
        return true;
    }
}
