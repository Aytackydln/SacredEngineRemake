using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

public sealed record GoldLobbyPublicProfileWork(GoldLobbyTransferEvent Event, GoldLobbyPublicProfile Profile, bool UpdatesLocal);
public sealed record GoldLobbyPlayerProfileWork(GoldLobbyTransferEvent Event, ReadOnlyMemory<byte> Profile, ushort PageIndex, bool NeedsPatchSource);

/// <summary>Profile normalization, player-row copying and ordered merge/UI work; full online service remains separate.</summary>
public sealed class GoldLobbyProfileTransfers
{
    private readonly List<GoldLobbyPublicProfileWork> _public = [];
    private readonly List<GoldLobbyPlayerProfileWork> _players = [];
    private readonly Dictionary<uint, ReadOnlyMemory<byte>> _patches = [];
    private readonly HashSet<uint> _knownPatchSources = [];
    private readonly Dictionary<uint, ReadOnlyMemory<byte>> _profiles = [];
    public GoldLobbyProfileTransfers()
    {
        PublicRequests = _public.AsReadOnly(); PlayerRequests = _players.AsReadOnly();
        PlayerProfiles = new ReadOnlyDictionary<uint, ReadOnlyMemory<byte>>(_profiles);
    }
    public IReadOnlyList<GoldLobbyPublicProfileWork> PublicRequests { get; }
    public IReadOnlyList<GoldLobbyPlayerProfileWork> PlayerRequests { get; }
    public IReadOnlyDictionary<uint, ReadOnlyMemory<byte>> PlayerProfiles { get; }
    public long ReceivedCount { get; private set; }
    public long UnsupportedProfileCount { get; private set; }
    public long UnsafeSelectorCount { get; private set; }
    /// <summary>Known result of the native current-profile lookup. Null means known missing; absent configuration means unknown.</summary>
    public void ConfigurePatchSource(uint selector, byte[]? bytes)
    {
        _knownPatchSources.Add(selector);
        if (bytes is null) _patches.Remove(selector); else _patches[selector] = bytes.ToArray();
    }
    internal void Apply(GoldLobbyTransferEvent incoming, GoldSessionRosterReplica roster)
    {
        var entry = GoldLobbyTransferCodec.Decode(incoming.Message); ReceivedCount++;
        if (entry.DecodedProfile is { } decoded)
        {
            var profile = GoldLobbyPublicProfile.Read(decoded.Span);
            if (profile is null) UnsupportedProfileCount++;
            else _public.Add(new(entry, profile, entry.Message.Opcode == GoldLobbyTransferCodec.UpdateOpcode));
        }
        if (entry.PlayerProfile is not { } raw) return;
        // Native uses this unchecked operand as an index into the 17-row controller roster.
        if (entry.Selector > 16) { UnsafeSelectorCount++; return; }
        var profileBytes = raw.ToArray(); var known = _knownPatchSources.Contains(entry.Selector);
        if (_patches.TryGetValue(entry.Selector, out var patch))
        {
            profileBytes.AsSpan(8, 128).Clear(); patch.Span[..Math.Min(patch.Length, 128)].CopyTo(profileBytes.AsSpan(8));
        }
        _players.Add(new(entry, profileBytes, unchecked((ushort)(entry.Selector - 1)), !known));
        if (!known) return;
        _profiles[entry.Selector] = profileBytes; roster.SetProfile((byte)entry.Selector, profileBytes);
    }
    internal void Clear()
    {
        _public.Clear(); _players.Clear(); _profiles.Clear(); _patches.Clear(); _knownPatchSources.Clear();
        ReceivedCount = UnsupportedProfileCount = UnsafeSelectorCount = 0;
    }
}
