namespace Sacred.GameplayHost.Scripting;

/// <summary>CreateObj's Int32 open/lock locals after queued UInt16 overrides.
/// FFFF preserves the incoming value; other halves are zero-extended.</summary>
public readonly record struct WorldScriptObjectCreationState(int Open, int Locked)
{
    internal WorldScriptObjectCreationState Apply(WorldScriptDeferredObjectState request) => new(
        request.Open == -1 ? Open : unchecked((ushort)request.Open),
        request.Locked == -1 ? Locked : unchecked((ushort)request.Locked));
}
