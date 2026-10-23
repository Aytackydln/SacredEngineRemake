namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x4AB160 active registry: first XYZ/surface wins; priority records insert at front.</summary>
public sealed class GoldMouseoverRegistry
{
    private readonly List<GoldSectorScriptPatch> _active = [];
    private readonly HashSet<(ushort X, ushort Y, ushort Surface)> _keys = [];
    public GoldMouseoverRegistry() => Active = _active.AsReadOnly();
    public IReadOnlyList<GoldSectorScriptPatch> Active { get; }
    public void Register(GoldSectorScriptPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (!_keys.Add((patch.X, patch.Y, patch.Surface))) return;
        if (patch.InsertAtFront) _active.Insert(0, patch);
        else _active.Add(patch);
    }
    internal void Clear() { _active.Clear(); _keys.Clear(); }
}
