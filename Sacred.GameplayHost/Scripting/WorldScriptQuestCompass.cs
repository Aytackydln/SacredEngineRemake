using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x49A4B0 literal startup caller. The local GUI event precedes
/// both real refreshes and journal mutation; startup suppresses only the network
/// update. Default function quest, named/object targets and live entry stay guarded.</summary>
public static class WorldScriptQuestCompass
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new NotSupportedException("QuestKompassPos live entry requires native journal publication.");
        var x = -1; var y = -1; int? number = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind != SacredScriptArgumentKind.Integer || operand.Integer is not { } value)
                throw new NotSupportedException("QuestKompassPos requires its compared literal coordinate and explicit quest operands.");
            if (x == -1) x = value;
            else if (y == -1) y = value;
            else number = value;
        }
        if (x < 0 || y < 0) return;
        if (number is null) throw new NotSupportedException("QuestKompassPos omitted quest requires native function quest save/restore.");
        var quests = context.Quests ?? throw new InvalidDataException("QuestKompassPos requires original journal state.");
        ApplyLiteral(quests, number.Value, (uint)x, (uint)y, 1200, context.LocalPlayerSlot, context.SubmitLocalGuiRequest);
    }

    public static void ApplyLiteral(WorldScriptQuests quests, int number, uint x, uint y, uint radius,
        uint? localPlayerSlot, Action<WorldScriptLocalGuiRequest> submit)
    {
        if (x > int.MaxValue || y > int.MaxValue || radius != 1200)
            throw new NotSupportedException("Compass literal helper requires the compared nonnegative coordinates and default radius.");
        var journal = quests.Journal;
        var index = journal.FindQuest(number);
        if (index < 0) { quests.SetPendingCompass(x, y, radius); return; }
        // The real helper narrows its index to UInt16, independently of the caller.
        var refreshIndex = unchecked((ushort)index);
        var oldX = journal.ReadWord(refreshIndex, SacredScriptJournalLayout.TargetXOffset);
        if (oldX is uint.MaxValue or 0xEEEEEEEE)
            throw new NotSupportedException("Existing compass object/resource target requires native lookup before literal replacement.");
        var selected = number < 100;
        if (!selected)
        {
            var owner = localPlayerSlot ?? throw new InvalidDataException("Other-quest compass selection requires the local controller slot.");
            // This caller uses the raw slot, unlike quest entry's clamped slot-minus-one.
            if (owner >= GoldActualQuestsLayout.SlotCount)
                throw new NotSupportedException("Other-quest compass controller slot lies beyond the compared index table.");
            selected = journal.GetIndex(owner, false) == index || journal.GetIndex(owner, true) == index;
        }
        if (selected && (journal.ReadWord(index, SacredScriptJournalLayout.TargetXOffset) != x ||
            journal.ReadWord(index, SacredScriptJournalLayout.TargetYOffset) != y))
            submit(new(0x34, 0, 0, 0, 0));
        Refresh(journal, refreshIndex);
        journal.WriteWord(index, SacredScriptJournalLayout.TargetXOffset, x);
        journal.WriteWord(index, SacredScriptJournalLayout.TargetYOffset, y);
        journal.WriteWord(index, SacredScriptJournalLayout.TargetRadiusOffset, radius);
        Refresh(journal, refreshIndex);
    }

    private static void Refresh(WorldScriptQuestJournal journal, int index)
    {
        var x = journal.ReadWord(index, SacredScriptJournalLayout.TargetXOffset);
        var y = journal.ReadWord(index, SacredScriptJournalLayout.TargetYOffset);
        if (x == 0 && y == 0) return;
        journal.WriteWord(index, SacredScriptJournalLayout.ResolvedTileXOffset, x);
        journal.WriteWord(index, SacredScriptJournalLayout.ResolvedTileYOffset, y);
    }
}
