using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Latest statistics by received sender/origin, retaining routing identity until relay behavior is verified.</summary>
public sealed class GoldPlayerStatisticsReplica
{
    private readonly Dictionary<(byte Sender, byte Origin), GoldPlayerStatisticsState> _states = [];
    public GoldPlayerStatisticsReplica() => States = new ReadOnlyDictionary<(byte Sender, byte Origin), GoldPlayerStatisticsState>(_states);
    public IReadOnlyDictionary<(byte Sender, byte Origin), GoldPlayerStatisticsState> States { get; }

    public void Apply(GoldPlayerStatisticsEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _states[(entry.Message.Sender, entry.Message.Origin)] = entry.State;
    }

    public void Clear() => _states.Clear();
}
