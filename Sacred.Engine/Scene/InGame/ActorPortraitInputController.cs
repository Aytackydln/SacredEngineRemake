using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.UI.Character;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Portrait gestures belong to the UI and cannot create world movement commands.</summary>
internal sealed class ActorPortraitInputController(ActorPortraitPanelState state, InputState input, Func<Vector2> viewport)
{
    private bool _captured;
    public void Update()
    {
        if (state.Player is null || state.Layout is not { } layout || input.ImGuiWantsMouse)
        { _captured = false; return; }
        var size = viewport();
        int? Hit(Vector2 point)
        {
            var count = Math.Min(state.Party.Members.Count, ActorPortraitLayout.MaximumCompanions);
            for (var index = 0; index <= count; index++) if (layout.Bounds(index, size).Contains(point)) return index;
            return null;
        }
        if (input.PendingLeftClickPosition is { } press && Hit(press) is { } index)
        {
            input.TryConsumeLeftClick(out _); _captured = true;
            EngineLog.WriteLine($"Debug input: {(index == 0 ? "player" : $"party member {index}")} portrait pressed.");
        }
        var captured = _captured;
        if (_captured && input.ConsumeLeftMouseButtonReleased() || !input.IsLeftMouseButtonDown) _captured = false;
        input.HudWantsMouse |= captured || Hit(input.MousePosition).HasValue;
    }
}
