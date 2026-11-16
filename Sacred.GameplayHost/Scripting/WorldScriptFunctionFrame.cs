namespace Sacred.GameplayHost.Scripting;

/// <summary>One Gold 0x46BA90 stack frame. Restart reuses the frame and
/// replaces its saved quest with the current quest before another dispatch.</summary>
internal sealed class WorldScriptFunctionFrame(int? restoreQuest)
{
    public int? RestoreQuest { get; set; } = restoreQuest;
    public bool RestartRequested { get; set; }
}
