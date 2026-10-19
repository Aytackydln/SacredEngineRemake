namespace Sacred.Network.Gameplay;

/// <summary>Original receiver guards and logical drop transition; no rendering or clock advancement.</summary>
internal static class GoldObjectFlightReducer
{
    public static bool Apply(Dictionary<uint, GoldReplicatedObject> objects, GoldObjectFlightEvent flight)
    {
        if (!objects.TryGetValue(flight.ObjectReference, out var obj) || obj.Physical is null ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return false;
        // Gold dispatch passes the retained origin (ESI at 0x7DE5E4), not the
        // transport sender. Relayed commands have server sender zero and host origin.
        if (!flight.Forced && (obj.BaseState.Owner != flight.Message.Origin || obj.Item?.InventoryId is > 0 ||
            obj.Item is { } item && (item.Instance.Flags & 0x200000) != 0)) return false;
        var basis = obj.BaseState;
        objects[flight.ObjectReference] = obj with
        {
            BaseState = basis with { X = flight.Position.X, Y = flight.Position.Y,
                Surface = unchecked((byte)flight.Position.Surface), Sector = unchecked((ushort)flight.Position.Sector),
                Word34 = 0, Flags = (basis.Flags & ~0x800602u) | GoldObjectFlightLayout.FlyingFlag },
            Flight = flight, FlightVersion = obj.FlightVersion + 1,
            PositionOpcode = flight.Message.Opcode, PositionVersion = obj.PositionVersion + 1
        };
        return true;
    }
}
