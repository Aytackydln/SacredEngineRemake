namespace Sacred.Network.Gameplay;

/// <summary>Receive-only weather cache; simulation belongs to GameplayHost.</summary>
public sealed class GoldWeatherReplica
{
    private readonly Queue<GoldWeatherNotification> _recent = [];
    public GoldWeatherState? Current { get; private set; }
    public long Revision { get; private set; }
    public long SnapshotRevision { get; private set; }
    public IReadOnlyCollection<GoldWeatherNotification> Recent => _recent;
    public void Apply(GoldWorldEnvironmentEvent environment)
    {
        if (environment.Message.Sender != 0 || environment.Weather.IsEmpty) return;
        Current = GoldWeatherState.FromPayload(environment.Weather);
        SnapshotRevision = ++Revision;
    }
    public void Apply(GoldWeatherEvent weather)
    {
        _recent.Enqueue(new(++Revision, weather with { Message = weather.Message with { Payload = weather.Message.Payload.ToArray() } }));
        if (_recent.Count > 256) _recent.Dequeue();
    }
    public void Clear() { Current = null; _recent.Clear(); SnapshotRevision = ++Revision; }
}

public sealed record GoldWeatherNotification(long Version, GoldWeatherEvent Event);
