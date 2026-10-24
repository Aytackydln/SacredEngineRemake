using System;
using System.Numerics;
using Windows.Gaming.Input;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameInputController
{
    public Func<PlayerCombatRequest, bool>? CombatActionRequested { get; set; }
    public Func<bool>? CombatActive { get; set; }

    private void UpdateCombatInput(bool uiWantsMouse)
    {
        if (uiWantsMouse || _input.UiWantsKeyboard) return;
        if (_input.IsDown(VirtualKey.Control) && _input.TryConsumeLeftClick(out var attackPoint))
            RequestCombat(false, attackPoint);
        if (_input.ConsumeRightMouseButtonPressed()) RequestCombat(true, _input.MousePosition);
        if (_gamepad.WasPressed(GamepadButtons.X)) RequestCombat(false, _input.MousePosition);
    }

    private void RequestCombat(bool cast, Vector2 point)
    {
        var screen = _outputToViewport(point);
        var target = GameActorElevation.ScreenToWorldOnSurface(_camera, _elevation, screen, _viewportWidth(), _viewportHeight());
        var self = _scene.Models.Count > 0 && WorldModelFocusPicker.TryScore(_scene.Models[0], _camera,
            screen, _viewportWidth(), _viewportHeight(), out _);
        CombatActionRequested?.Invoke(new(cast, self ? _camera.WorldCenter : target, self));
    }
}
