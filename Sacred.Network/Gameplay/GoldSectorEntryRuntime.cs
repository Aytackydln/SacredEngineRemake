using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Mapped local Gold sector +0x30 interest/receive bits, not the complete native runtime flags.
/// These are separate from KEYX environment flags and host region flags.</summary>
public sealed class GoldSectorEntryRuntime
{
    public const uint InitializedFlag = 0x400;
    public const uint EnteredFlag = 0x800;
    private readonly Dictionary<ushort, uint> _flags = [];
    public GoldSectorEntryRuntime() => Flags = new ReadOnlyDictionary<ushort, uint>(_flags);
    public IReadOnlyDictionary<ushort, uint> Flags { get; }

    internal void Begin(GoldSectorInterestRequest request)
    {
        // 0x626610..0x626671 marks entered sectors before sending; 0x6262A0 clears departed 0x800 afterward.
        for (var index = 0; index < GoldSectorInterestLayout.SlotCount; index++)
        {
            var entered = request.GetEnteredSector(index);
            if (entered != 0)
            {
                _flags.TryGetValue(entered, out var flags);
                _flags[entered] = flags | InitializedFlag | EnteredFlag;
            }
            var left = request.GetLeftSector(index);
            if (left != 0)
            {
                _flags.TryGetValue(left, out var previous);
                _flags[left] = previous & ~EnteredFlag;
            }
        }
    }

    /// <summary>0x7E56E0: a script-mode reply runs only when this local sector was previously entered.</summary>
    internal bool Receive(GoldSectorEntrySlot entry)
    {
        _flags.TryGetValue(entry.Sector, out var flags);
        // 0x626130 sets 0x400 before invoking the initializer; flags do not establish completion.
        flags |= InitializedFlag;
        var executeScripts = entry.RequestsScripts && (flags & EnteredFlag) != 0;
        // The receive loop clears 0x800, then 0x6261A0 sets it before calling the script producer.
        if (executeScripts || !entry.RequestsScripts) flags |= EnteredFlag;
        _flags[entry.Sector] = flags;
        return executeScripts;
    }

    internal void Clear() => _flags.Clear();
}
