namespace Sacred.Network.Discovery;

/// <summary>Retains the latest advertised state and expires silent servers after the native five seconds.</summary>
public sealed class GoldLanServerCatalog
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private readonly Dictionary<(string Endpoint, string Name), (GoldLanAnnouncement Server, TimeSpan Seen)> _servers = [];
    public IReadOnlyList<GoldLanAnnouncement> Servers { get; private set; } = [];

    public bool Apply(GoldLanAnnouncement announcement, TimeSpan now)
    {
        var key = (announcement.Endpoint.ToString(), announcement.Name);
        var changed = !_servers.TryGetValue(key, out var previous) || previous.Server != announcement;
        _servers[key] = (announcement, now);
        if (changed) Publish();
        return changed;
    }

    public bool Expire(TimeSpan now)
    {
        var expired = _servers.Where(entry => now - entry.Value.Seen >= Lifetime).Select(entry => entry.Key).ToArray();
        foreach (var key in expired) _servers.Remove(key);
        if (expired.Length != 0) Publish();
        return expired.Length != 0;
    }

    private void Publish() => Servers = _servers.Values.Select(entry => entry.Server)
        .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ThenBy(server => server.Endpoint.ToString(), StringComparer.Ordinal)
        .ToArray();
}
