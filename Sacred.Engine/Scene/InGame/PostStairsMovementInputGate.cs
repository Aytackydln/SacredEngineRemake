using System.Numerics;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Requires movement controls to return to rest after a stairs transition.</summary>
internal sealed class PostStairsMovementInputGate
{
    private const float ControllerResetRadius = 0.35f;

    private InputGateState _mouseState;
    private InputGateState _controllerState;
    private InputGateState _keyboardState;

    public MovementInputAvailability Update(InputState input)
    {
        _mouseState = UpdateMouseState(_mouseState, input);
        _controllerState = UpdateState(_controllerState, IsControllerMovementActive(input));
        _keyboardState = UpdateState(_keyboardState, IsKeyboardMovementActive(input));

        return new MovementInputAvailability(
            _mouseState == InputGateState.Open,
            _controllerState == InputGateState.Open,
            _keyboardState == InputGateState.Open);
    }

    public void BlockUntilNewInput(InputState input)
    {
        _mouseState = input.IsLeftMouseButtonDown
            ? InputGateState.WaitingForReset
            : InputGateState.WaitingForNewInput;
        _controllerState = IsControllerMovementActive(input)
            ? InputGateState.WaitingForReset
            : InputGateState.WaitingForNewInput;
        _keyboardState = IsKeyboardMovementActive(input)
            ? InputGateState.WaitingForReset
            : InputGateState.WaitingForNewInput;
    }

    private static InputGateState UpdateMouseState(InputGateState state, InputState input) => state switch
    {
        InputGateState.WaitingForReset when !input.IsLeftMouseButtonDown => InputGateState.WaitingForNewInput,
        InputGateState.WaitingForNewInput when input.IsLeftMouseButtonDown && input.HasPendingLeftClick =>
            InputGateState.Open,
        _ => state
    };

    private static InputGateState UpdateState(InputGateState state, bool movementActive) => state switch
    {
        InputGateState.WaitingForReset when !movementActive => InputGateState.WaitingForNewInput,
        InputGateState.WaitingForNewInput when movementActive => InputGateState.Open,
        _ => state
    };

    private static bool IsControllerMovementActive(InputState input)
    {
        var stick = new Vector2((float)input.LeftJoystickX, (float)input.LeftJoystickY);
        return stick.LengthSquared() > ControllerResetRadius * ControllerResetRadius;
    }

    private static bool IsKeyboardMovementActive(InputState input) =>
        input.IsDown(VirtualKey.Left) || input.IsDown(VirtualKey.A) ||
        input.IsDown(VirtualKey.Right) || input.IsDown(VirtualKey.D) ||
        input.IsDown(VirtualKey.Up) || input.IsDown(VirtualKey.W) ||
        input.IsDown(VirtualKey.Down) || input.IsDown(VirtualKey.S);

    private enum InputGateState
    {
        Open,
        WaitingForReset,
        WaitingForNewInput
    }
}

internal readonly record struct MovementInputAvailability(
    bool Mouse,
    bool Controller,
    bool Keyboard)
{
    public static MovementInputAvailability All { get; } = new(true, true, true);
}
