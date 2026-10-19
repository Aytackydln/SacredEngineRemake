namespace Sacred.Network.Gameplay;

/// <summary>A requested native operation, identified across replies and session resets.</summary>
public sealed record GoldSectorWork(long Session, long Id, long Reply, int Slot,
    GoldSectorEntrySlot Entry, GoldSectorWorkState State = GoldSectorWorkState.Queued, string? Error = null)
{
    public bool ExecuteScripts { get; init; } = Entry.RequestsScripts;
    /// <summary>The zero-mode receive branch and the completed native EnterSector
    /// producer both run 0x493440, followed by the receiver's local enter notification.</summary>
    public bool ApplyPatches => Entry.ScriptMode == 0 || ExecuteScripts;
    /// <summary>Retain the pending vectors present when this reply arrived. Loading terrain
    /// asynchronously must not substitute a later bank for a previously queued operation.</summary>
    public GoldWorldBankSnapshot? PatchBank { get; init; }
    public GoldWorldBankSnapshot? PositionBank { get; init; }
}

public enum GoldSectorWorkState { Queued, Executing, Completed, Failed, Skipped }
