using Sacred.Network.Gameplay;
using Sacred.World.Objects;

namespace Sacred.World.Networking;

/// <summary>Authored door footprints linked by their original trigger names to received trigger slots.</summary>
public sealed class SacredNetworkDoorStates
{
    private readonly GoldWorldReplica _world;
    private readonly WorldDoorDefinition[] _definitions;
    private long _revision = -1;
    private long _namesRevision = -1;
    private readonly Dictionary<uint, uint> _triggers = [];
    public SacredNetworkDoorStates(GoldWorldReplica world, IEnumerable<WorldDoorDefinition> definitions)
    {
        _world = world;
        _definitions = definitions.DistinctBy(door => door.Id).ToArray();
        States = new(_definitions);
        Update();
    }
    public WorldDoorStateLayer States { get; }
    public void Update()
    {
        if (_revision == _world.Revision) return;
        _revision = _world.Revision;
        if (_namesRevision != _world.ScriptResources.Revision)
        {
            _namesRevision = _world.ScriptResources.Revision;
            _triggers.Clear();
            foreach (var door in _definitions)
                if (door.TriggerName is { } name && _world.ScriptResources.FindName(name) is { } named)
                    _triggers[door.Id] = named.TriggerReference;
        }
        foreach (var pair in _triggers)
            if (_world.WorldTriggers.Records.TryGetValue(pair.Value, out var trigger))
                States.ApplyReceivedState(pair.Key, (trigger.State & 1) != 0, (trigger.State & 0x8000) != 0);
    }
}
