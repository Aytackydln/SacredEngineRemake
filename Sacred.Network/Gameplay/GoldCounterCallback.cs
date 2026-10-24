namespace Sacred.Network.Gameplay;

/// <summary>Receipt-time native callback request; resolution does not execute the function.</summary>
public sealed record GoldCounterCallback(GoldCounterRecord Counter, GoldScriptFunctionSnapshot Functions)
{
    public bool TryResolve(out uint? index)
    {
        // Gold's empty/oversized name branch calls function slot zero, rather than treating it as no callback.
        var name = Counter.CallbackName;
        if (name.Length is 0 or >= GoldCounterLayout.NameSize) { index = 0; return true; }
        if (!Functions.TryGetDefinitions(out var definitions)) { index = null; return false; }
        for (var i = 0; i < definitions.Count; i++)
            if (GoldScriptNames.Equal(name, definitions[i].Name)) { index = (uint)i; return true; }
        index = null; return true;
    }
}

/// <summary>Native network collection attempt. Inventory amount calculation, insertion/removal and sound are deferred.</summary>
public sealed record GoldCounterCollectionAttempt(GoldCounterCollectEvent Event, GoldReplicatedObject? Source,
    GoldReplicatedObject? Hero, byte LocalPlayerSlot);
