namespace Sacred.Network.Gameplay;

/// <summary>Named 110 invocation with the function vector frozen at receipt. Resolution does not execute a script.</summary>
public sealed record GoldScriptInvocation(GoldScriptInvokeEvent Event, GoldScriptFunctionSnapshot Functions)
{
    public bool TryResolve(out uint? index)
    {
        var name = Event.FunctionName;
        // Native 46BF40 invokes slot zero for empty or >=64-byte names, even with an empty table.
        if (name.Length is 0 or >= 64) { index = 0; return true; }
        if (!Functions.TryGetDefinitions(out var definitions)) { index = null; return false; }
        for (var i = 0; i < definitions.Count; i++)
            if (GoldScriptNames.Equal(name, definitions[i].Name)) { index = (uint)i; return true; }
        index = null; return true;
    }
}

public sealed class GoldScriptInvocationReplica
{
    private readonly List<GoldScriptInvocation> _requests = [];
    public GoldScriptInvocationReplica() => Requests = _requests.AsReadOnly();
    public IReadOnlyList<GoldScriptInvocation> Requests { get; }
    internal void Apply(GoldScriptInvokeEvent entry, GoldScriptFunctionReplica functions) =>
        _requests.Add(new((GoldScriptInvokeEvent)GoldReceiveActionCodec.Decode(entry.Message), functions.Capture()));
    internal void Clear() => _requests.Clear();
}
