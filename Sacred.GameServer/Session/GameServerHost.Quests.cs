using System.Buffers.Binary;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Authoritative mode: quest pairs of the heroes (<see cref="QuestRecords"/>). A 0x145 <c>[key][word04]</c> from a player is
/// applied by its own client to its hero and by every client that receives it to that client's hero (Sacred.exe 0x5D8280,
/// receive 0x7DE5F3), so the server applies it to the sender's and to each recipient's latest inventory stream. The recipients
/// are the ones <see cref="Forward"/> sends the party-mode (5) message to; while parties are not modelled that is every other
/// in-game player, and those clients do append the pair. Hero state (chunk CD, the explored map) is never on the wire.
/// </summary>
public sealed partial class GameServerHost
{
    /// <summary>Quest pairs appended to a hero's inventory from 0x145 (diagnostics and tests).</summary>
    public int QuestRecordsApplied { get; private set; }

    private void CaptureQuestRecord(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < QuestRecords.PayloadBytes) return;
        var key = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        var word04 = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]);
        ApplyQuestRecord(s, key, word04);
        for (var slot = 1; slot <= MaxSlots; slot++)                         // the recipients of Forward(s, m, Party, false)
            if (_slots[slot] is { } target && target != s && target.State >= SessionState.SlotAssigned && target.InGame)
                ApplyQuestRecord(target, key, word04);
    }

    private void ApplyQuestRecord(PlayerSession p, uint key, uint word04)
    {
        if (_live[p.Slot] is not { } live || QuestRecords.Apply(live, key, word04) != QuestRecords.Result.Appended) return;
        QuestRecordsApplied++;
        MarkHeroChanged(p);
    }
}
