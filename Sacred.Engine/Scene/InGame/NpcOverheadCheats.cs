using System;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private bool TrySetNpcOverheads(string option, string value, out string message)
    {
        message = string.Empty;
        if (option.Equals("npc-overhead-time", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Equals("live", StringComparison.OrdinalIgnoreCase)) _scene.NpcOverheads.AnimationMillisecondsOverride = null;
            else if (uint.TryParse(value, out var milliseconds)) _scene.NpcOverheads.AnimationMillisecondsOverride = milliseconds;
            else { message = "use set npc-overhead-time <live|milliseconds>"; return true; }
            message = $"NPC overhead animation clock: {_scene.NpcOverheads.AnimationMillisecondsOverride?.ToString() ?? "live"}";
            EngineLog.WriteLine($"Debug input: {message}.");
            return true;
        }
        if (!option.Equals("npc-overheads", StringComparison.OrdinalIgnoreCase)) return false;
        switch (value.ToLowerInvariant())
        {
            case "alt": _scene.NpcOverheads.ShowStatesOverride = true; break;
            case "normal": _scene.NpcOverheads.ShowStatesOverride = false; break;
            case "input": _scene.NpcOverheads.ShowStatesOverride = null; break;
            case "status": break;
            default: message = "use set npc-overheads <alt|normal|input|status>"; return true;
        }
        var state = _scene.NpcOverheads;
        state.ShowStates = state.ShowStatesOverride ?? (!_window.Input.UiWantsKeyboard && _window.Input.IsDown(VirtualKey.Alt));
        message = $"NPC overheads: {state.ArtworkStatus}; {state.Actors.Count} received actor instances; {state.DrawnIcons} icons drawn ({state.DrawnGoldenIcons} golden); ALT states {state.ShowStates}, control {state.ShowStatesOverride?.ToString() ?? "keyboard"}";
        message += $"; glow layers {state.DrawnGlowLayers}, animation clock {state.AnimationMilliseconds} ms";
        if (_networkPresentation?.Conversations is { } conversations)
            message += $"; conversations {conversations.ReceivedCount}, local states {conversations.States.Count}, waiting for animation/speed {conversations.NeedsAnimationCount}, waiting for AI {conversations.NeedsAiResumeCount}";
        EngineLog.WriteLine($"Debug input: {message}.");
        return true;
    }
}
