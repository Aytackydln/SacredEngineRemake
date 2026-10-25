using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Ordered localized HUD notices; a missing resource defers its own channel without replaying counters.</summary>
public sealed class GameplayReceivedHud
{
    private readonly GoldWorldReplica _world;
    private readonly GoldConsumerCursor _cursor;
    private readonly List<GameplayHudNotice> _notices = [];
    private long _session = -1;
    public GameplayReceivedHud(GoldWorldReplica world)
    {
        _world = world;
        _cursor = world.Consumers.CreateCursor("hud-notices", work => work.Family == "hud");
    }
    public IReadOnlyList<GameplayHudNotice> Notices => _notices;
    public void Advance(float seconds, GameResStore resources)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _world.WorldBanks.Counters.Messages.ConfigureResources(resources);
        if (_session != _world.Consumers.Session) { _notices.Clear(); _session = _world.Consumers.Session; }
        foreach (var notice in _notices) notice.Advance(seconds);
        _notices.RemoveAll(notice => notice.Expired);
        while (_world.Consumers.TryPeek(_cursor, out var work))
        {
            var (mode, flags, text) = work.Value switch
            {
                GoldCounterHudMessage message => (message.Mode, message.Flags, _world.WorldBanks.Counters.Messages.Resolve(message)),
                GoldRegionCountdownWarning warning => (warning.Mode, warning.Flags,
                    resources.Strings.GetValueOrDefault(SacredResourceHash.Compute(warning.ResourceKey))),
                _ => (0u, 0u, (string?)null)
            };
            if (mode is not (1 or 2 or 3 or 4 or 0x10) || work.Opcode == 0x19E && (flags & 0x80000000) != 0)
            {
                _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Rejected,
                    mode is not (1 or 2 or 3 or 4 or 0x10) ? $"HUD mode {mode} has no text-line renderer" : "wire process-string operand cannot be dereferenced");
                continue;
            }
            if (text is null)
            {
                _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Deferred, "original text resource unavailable");
                break;
            }
            _notices.Add(new(text, flags & 0xFFFF, work.Order, mode));
            _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, $"mode {mode}; font {flags & 0xFFFF}; {text}");
        }
    }
}
