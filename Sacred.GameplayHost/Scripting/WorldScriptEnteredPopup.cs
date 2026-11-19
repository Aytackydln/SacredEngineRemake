using System.Buffers.Binary;

namespace Sacred.GameplayHost.Scripting;

/// <summary>4A78C2..4A79C3 owned local-hero POPUP. Its explicit dialog update
/// precedes pending 54CBB0 action state; the later AI/UI tick is separate.</summary>
internal static class WorldScriptEnteredPopup
{
    public static (WorldCreatureInitialActor Dialog, WorldCreatureInitialActor Pending) Prepare(
        WorldCreatureInitialActor actor, WorldScriptRegistrationTable table, int selected)
    {
        if (selected <= 0 || selected >= table.Count || actor.Base.ObjectReference is 0 or > 16 ||
            actor.Base.Owner != actor.Base.ObjectReference || actor.DialogIndex == 0 && (actor.RuntimeFlags & 0x40) == 0)
            throw new NotSupportedException("POPUP requires its compared local owner and explicit/disabled-default dialog getter.");
        table.AttachState(actor.Base.ObjectReference, actor.DialogIndex, selected);
        var dialog = actor with { DialogIndex = checked((uint)selected) };
        var ki = actor.KiState.ToArray(); var ei = actor.EiState.ToArray();
        if (BinaryPrimitives.ReadUInt16LittleEndian(ei) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(ei.AsSpan(2)) > 1)
            BinaryPrimitives.WriteUInt16LittleEndian(ei.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(ei, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(ki, 10);
        BinaryPrimitives.WriteUInt16LittleEndian(ki.AsSpan(2), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(ki.AsSpan(4), actor.Base.ObjectReference);
        BinaryPrimitives.WriteUInt32LittleEndian(ki.AsSpan(8), 1);
        return (dialog, dialog with { KiState = ki, EiState = ei, RuntimeFlags = actor.RuntimeFlags | 0x10u });
    }
}
