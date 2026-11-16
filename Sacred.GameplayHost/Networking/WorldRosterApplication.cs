using System.Buffers.Binary;
using Sacred.Core.Pax.Hero;
using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x7E4C20 valid-table profile with the resource singleton present.
/// Incoming wire records and locally applied records remain distinct. Caller
/// owns service identities and queues/delivers the returned effects.</summary>
public static class WorldRosterApplication
{
    public static WorldRosterApplicationStage Apply(GoldPlayerRoster previous, GoldPlayerRoster incoming,
        byte localSlot, bool controllerFlag28, bool multiplayer, Func<string, string> identifier)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(identifier);
        if (localSlot is < 1 or > 16) throw new NotSupportedException("Roster application requires a compared local slot.");
        var bytes = incoming.ToPayload();
        previous.GetEntry(0).Profile.CopyTo(bytes.AsSpan(GoldPlayerDataLayout.ProfileOffset));
        var roster = GoldPlayerRoster.FromPayload(bytes);
        var rows = new List<WorldRosterAppliedRow>();
        var notices = new List<byte>();
        for (byte slot = 1; slot <= 16; slot++)
        {
            // Native string assignment is NUL-terminated, rather than bounded
            // to the name field. Preserve code units without Unicode replacement.
            var name = ReadName(bytes.AsSpan(slot * GoldPlayerDataLayout.Size + GoldPlayerDataLayout.NameOffset));
            var record = roster.GetEntry(slot);
            rows.Add(new(slot, name, identifier(name), multiplayer && record.ConnectionSpeed == 1,
                BinaryPrimitives.ReadUInt32LittleEndian(record.Profile[SacredHeroProfileLayout.RosterUiGateOffset..]) != 0));
            if (controllerFlag28 && slot != localSlot && record.IsPresent && !previous.GetEntry(slot).IsPresent)
                notices.Add(slot);
        }
        return new(roster, rows.AsReadOnly(), notices.AsReadOnly());
    }

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var chars = new List<char>();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes[i..]);
            if (value == 0) return new string(chars.ToArray());
            chars.Add((char)value);
        }
        throw new NotSupportedException("Native roster name requires termination beyond the compared table storage.");
    }
}
