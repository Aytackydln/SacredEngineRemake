using System.Numerics;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

public sealed partial class SacredCamera
{
    /// <summary>Console locomotion request in world coordinates; bypasses platform input.</summary>
    internal Vector2? DebugManualDirection { get; set; }

    internal Vector2 GetManualDirection(InputState input, MovementInputAvailability availability)
    {
        var direction = MovementDirection(input, availability, out _, out var rotationOnly);
        return DebugManualDirection ?? (direction != Vector2.Zero ? direction : rotationOnly);
    }

    private void ApplyConsoleMovement(ref Vector2 delta, ref float joystickMovementScale, ref Vector2 joystickRotationOnly)
    {
        if (DebugManualDirection is { } debugDirection)
        {
            delta = debugDirection;
            joystickMovementScale = 1;
            joystickRotationOnly = Vector2.Zero;
        }
    }
}
