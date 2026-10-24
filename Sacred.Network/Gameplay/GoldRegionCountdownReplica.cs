using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Native 64-byte region record +38/+3C operands and +34 one-shot warning latch.</summary>
public sealed record GoldRegionCountdown(uint Remaining, uint SelectorWord, bool WarningIssued);
public sealed record GoldRegionCountdownWarning(byte RegionIndex, string ResourceKey)
{
    public uint Mode => 0x10;
    public uint Flags => 3;
}

/// <summary>17E narrows its index to one byte. No player-slot range filter is present in Gold 7F6780.</summary>
public sealed class GoldRegionCountdownReplica
{
    private readonly Dictionary<byte, GoldRegionCountdown> _regions = [];
    private readonly List<GoldRegionCountdownWarning> _warnings = [];
    public GoldRegionCountdownReplica()
    {
        Regions = new ReadOnlyDictionary<byte, GoldRegionCountdown>(_regions); Warnings = _warnings.AsReadOnly();
    }
    public IReadOnlyDictionary<byte, GoldRegionCountdown> Regions { get; }
    public IReadOnlyList<GoldRegionCountdownWarning> Warnings { get; }
    public long ReceivedCount { get; private set; }
    internal void Apply(GoldRegionCountdownEvent incoming)
    {
        var entry = (GoldRegionCountdownEvent)GoldReceiveActionCodec.Decode(incoming.Message);
        var warned = _regions.GetValueOrDefault(entry.RegionIndex)?.WarningIssued == true;
        var issue = !warned && entry.Remaining is > 0 and < 1000;
        _regions[entry.RegionIndex] = new(entry.Remaining, entry.SelectorWord, warned || issue);
        if (issue)
        {
            // The current latch is set before Gold scans records 1..9 for the resource selection.
            var all = Enumerable.Range(1, 9).All(i => _regions.GetValueOrDefault((byte)i)?.WarningIssued == true);
            _warnings.Add(new(entry.RegionIndex, all ? "UI_REGION_ANCARIA" : "UI_REGION_FINALBATTLE"));
        }
        ReceivedCount++;
    }
    internal void Clear() { _regions.Clear(); _warnings.Clear(); ReceivedCount = 0; }
}
