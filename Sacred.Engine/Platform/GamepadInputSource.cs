using Windows.Gaming.Input;

namespace Sacred.Engine.Platform;

/// <summary>Polls one gamepad and exposes edge-triggered buttons to the active scene.</summary>
internal sealed class GamepadInputSource
{
    private GamepadButtons _previousButtons;

    public GamepadButtons Buttons { get; private set; }
    public GamepadButtons PressedButtons { get; private set; }

    public bool WasPressed(GamepadButtons button) => (PressedButtons & button) != 0;

    public void Poll(InputState input)
    {
        var gamepads = Gamepad.Gamepads;
        var gamepad = gamepads.Count == 0 ? null : gamepads[0];
        if (gamepad is null)
        {
            Buttons = GamepadButtons.None;
            PressedButtons = GamepadButtons.None;
            _previousButtons = GamepadButtons.None;
            input.LeftJoystickX = 0.0;
            input.LeftJoystickY = 0.0;
            input.RightJoystickY = 0.0;
            input.GamepadMoveFaster = false;
            return;
        }

        var reading = gamepad.GetCurrentReading();
        Buttons = reading.Buttons;
        PressedButtons = Buttons & ~_previousButtons;
        _previousButtons = Buttons;
        input.LeftJoystickX = reading.LeftThumbstickX;
        input.LeftJoystickY = reading.LeftThumbstickY;
        input.RightJoystickY = reading.RightThumbstickY;
        input.GamepadMoveFaster = (Buttons & GamepadButtons.A) != 0;
    }
}
