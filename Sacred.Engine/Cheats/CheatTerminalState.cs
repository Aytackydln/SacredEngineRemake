using System.Collections.Generic;

namespace Sacred.Engine.Cheats;

/// <summary>Shares terminal visibility and queued commands between rendering and simulation.</summary>
internal sealed class CheatTerminalState
{
    public bool IsOpen { get; private set; }
    public bool FocusInputRequested { get; set; }
    public Queue<string> Commands { get; } = new();

    public void SetOpen(bool open)
    {
        if (IsOpen == open)
            return;
        IsOpen = open;
        FocusInputRequested = open;
        EngineLog.WriteLine($"Debug input: cheat terminal {(open ? "opened" : "closed")}");
    }
}
