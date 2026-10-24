using System.Collections.Frozen;
using Sacred.Core.GameRes;

namespace Sacred.Network.Gameplay;

public sealed record GoldCounterHudMessage(uint Mode, uint Flags, uint? TextResource, uint? TargetResource = null,
    uint? Completed = null, uint? Initial = null);

/// <summary>Ordered native HUD requests. The bounded diagnostic history is a managed retention policy.</summary>
public sealed class GoldCounterMessages
{
    private readonly List<GoldCounterHudMessage> _recent = [];
    private FrozenDictionary<uint, uint> _itemResources = FrozenDictionary<uint, uint>.Empty;
    private GameResStore _resources = GameResStore.Empty;
    public GoldCounterMessages() => Recent = _recent.AsReadOnly();
    public IReadOnlyList<GoldCounterHudMessage> Recent { get; }
    public long ReceivedCount { get; private set; }
    public void ConfigureResources(GameResStore resources) => _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    internal void ConfigureItemResources(IReadOnlyDictionary<uint, uint> resources) => _itemResources = resources.ToFrozenDictionary();
    public string? Resolve(GoldCounterHudMessage message)
    {
        if (message.TextResource is { } text) return _resources.Strings.GetValueOrDefault(text);
        if (message.TargetResource is not { } target) return null;
        var resource = target > 0 && target < GoldCounterLayout.ResourceLimit ? _itemResources.GetValueOrDefault(target) : 0;
        if (!_resources.Strings.TryGetValue(resource, out var name) ||
            !_resources.Strings.TryGetValue(SacredResourceHash.Compute("COUNT_MIDDLE"), out var middle)) return null;
        // Native %d formatting treats the wrapping count operands as signed 32-bit values.
        return $"{name}:{unchecked((int)message.Completed!.Value)} {middle} {unchecked((int)message.Initial!.Value)}";
    }
    internal void Add(GoldCounterHudMessage message)
    {
        _recent.Add(message); ReceivedCount++;
        if (_recent.Count > GoldChatReplica.HistoryLimit) _recent.RemoveAt(0);
    }
    internal void Clear() { _recent.Clear(); ReceivedCount = 0; }
}
