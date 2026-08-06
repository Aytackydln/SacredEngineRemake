using System;
using Windows.Gaming.Input;

namespace Sacred.Engine.Platform;

/// <summary>Polls one gamepad and exposes edge-triggered buttons to the active scene.</summary>
internal sealed class GamepadInputSource
{
    private GamepadButtons _previousButtons;

    public GamepadButtons Buttons { get; private set; }
    public GamepadButtons PressedButtons { get; private set; }

    public bool WasPressed(GamepadButtons button) => (PressedButtons & button) != 0;
    public bool IsDown(GamepadButtons button) => (Buttons & button) != 0;

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
            input.GamepadWalk = false;
            input.GamepadDefend = false;
            return;
        }

        var reading = gamepad.GetCurrentReading();
        if (reading.Buttons != GamepadButtons.None ||
            Math.Abs(reading.LeftThumbstickX) > 0.01 ||
            Math.Abs(reading.LeftThumbstickY) > 0.01 ||
            Math.Abs(reading.RightThumbstickX) > 0.01 ||
            Math.Abs(reading.RightThumbstickY) > 0.01 ||
            reading.LeftTrigger > 0.01 ||
            reading.RightTrigger > 0.01)
        {
            input.MarkControllerInput();
        }
        Buttons = reading.Buttons;
        PressedButtons = Buttons & ~_previousButtons;
        _previousButtons = Buttons;
        input.LeftJoystickX = reading.LeftThumbstickX;
        input.LeftJoystickY = reading.LeftThumbstickY;
        input.RightJoystickY = reading.RightThumbstickY;
        input.GamepadWalk = (Buttons & GamepadButtons.A) != 0;
        input.GamepadDefend = reading.LeftTrigger >= 0.5;
    }
}
