using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

public sealed class SacredCamera
{
    public const float WalkingBaseSpeed = 2.0f;
    public const float RunningBaseSpeed = 12.0f;
    private const float JoystickDeadzone = 0.1f;
    private const float JoystickAntiDeadzone = 0.2f;
    private const float JoystickMaximumMovementInput = 0.8f;
    private const float JoystickRotationDeadzone = 0.075f;

    private static readonly Matrix3x2 IsometricRotation = Matrix3x2.CreateRotation(-MathF.PI / 4);

    public Vector2 WorldCenter { get; private set; }
    public float Zoom { get; private set; } = 1.0f;
    public float ViewportZoom => _viewportZoom;
    public Vector3 EyePosition { get; private set; }
    public Matrix4x4 View { get; private set; }
    public Matrix4x4 Projection { get; private set; }

    private int _viewportWidth;
    private int _viewportHeight;
    private readonly float _worldViewHeight;
    private readonly DirectionalPathfindingController _gamepadPathfinding = new();
    private float _viewportZoom;
    private Vector2? _movementTarget;

    public Vector2 CameraSpeedUnitVector { get; private set; } = Vector2.Zero;
    public Vector2 CharacterFacingUnitVector { get; private set; } = Vector2.Zero;
    public float CurrentMovementSpeed { get; private set; }
    public float LocomotionAnimationSpeed { get; private set; } = 1.0f;

    private SacredCamera(int width, int height)
    {
        _viewportWidth = Math.Max(1, width);
        _viewportHeight = Math.Max(1, height);
        _worldViewHeight = _viewportHeight;
        RebuildMatrices();
    }

    public static SacredCamera CreateDefault(int width, int height) => new(width, height);

    /// <summary>
    /// Updates the aspect ratio used by the 3D projection. The vertical game-world span remains fixed,
    /// so resizing changes only the horizontal extent of the 3D camera.
    /// </summary>
    public void SetViewportSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _viewportWidth && height == _viewportHeight)
            return;

        _viewportWidth = width;
        _viewportHeight = height;
        RebuildMatrices();
    }

    public void CenterOnTile(float x, float y, float? zoom = null)
    {
        WorldCenter = new Vector2(x, y);
        if (zoom is { } value)
            Zoom = Math.Clamp(value, 0.25f, 3.0f);
        RebuildMatrices();
    }

    public Vector2 ScreenToWorld(Vector2 screenPosition, int viewportWidth, int viewportHeight) =>
        IsometricProjection.ScreenToWorld(screenPosition, WorldCenter, ViewportZoom, viewportWidth, viewportHeight);

    public void MoveTo(Vector2 worldTarget) => _movementTarget = worldTarget;

    public void RotateToward(Vector2 direction)
    {
        if (direction.LengthSquared() > float.Epsilon)
            CharacterFacingUnitVector = Vector2.Normalize(direction);
    }

    public void StopMoving()
    {
        _movementTarget = null;
        _gamepadPathfinding.Reset();
        CameraSpeedUnitVector = Vector2.Zero;
        CurrentMovementSpeed = 0.0f;
    }

    public void UpdateFromInput(
        InputState input,
        float dt,
        WorldCollisionResolver collision,
        bool noClipEnabled = false)
    {
        var previousWorldCenter = WorldCenter;
        var previousZoom = Zoom;
        var baseSpeed = input.IsWalkModifierDown ? WalkingBaseSpeed : RunningBaseSpeed;
        var delta = MovementDirection(
            input,
            out var joystickMovementScale,
            out var joystickRotationOnly);
        CurrentMovementSpeed = 0.0f;
        LocomotionAnimationSpeed = 1.0f;

        if (input.IsDefendDown)
        {
            _movementTarget = null;
            CameraSpeedUnitVector = Vector2.Zero;
            if (delta.LengthSquared() > 0.0f)
                RotateToward(delta);
            else if (joystickRotationOnly.LengthSquared() > 0.0f)
                RotateToward(joystickRotationOnly);
        }
        else if (joystickRotationOnly.LengthSquared() > 0.0f)
        {
            _movementTarget = null;
            RotateToward(joystickRotationOnly);
        }

        if (input.IsDefendDown)
        {
            // Movement input is consumed as facing input while guarding.
        }
        else if (delta.LengthSquared() > 0)
        {
            _movementTarget = null;
            if (joystickMovementScale > 0.0f)
            {
                var speed = baseSpeed * joystickMovementScale;
                if (noClipEnabled)
                {
                    _gamepadPathfinding.Reset();
                    MoveInDirection(delta, speed, dt, collision, true);
                }
                else if (_gamepadPathfinding.TryGetWaypoint(WorldCenter, delta, collision, out var waypoint))
                    MoveInDirection(waypoint - WorldCenter, speed, dt, collision, false);
                else
                    RotateToward(delta);
            }
            else
            {
                _gamepadPathfinding.Reset();
                MoveInDirection(delta, baseSpeed, dt, collision, noClipEnabled);
            }
        }
        else if (_movementTarget is { } target)
        {
            _gamepadPathfinding.Reset();
            MoveTowardTarget(target, baseSpeed, dt, collision, noClipEnabled);
        }
        else
        {
            _gamepadPathfinding.Reset();
            CameraSpeedUnitVector = Vector2.Zero;
        }

        if (CurrentMovementSpeed > 0.0f)
            LocomotionAnimationSpeed = CurrentMovementSpeed / baseSpeed;

        if (input.IsDown(VirtualKey.Q)) Zoom *= MathF.Pow(0.985f, dt * 60f);
        if (input.IsDown(VirtualKey.E)) Zoom *= MathF.Pow(1.015f, dt * 60f);

        var rightStickZoom = ApplyDeadzone((float)input.RightJoystickY);
        if (rightStickZoom != 0.0f)
            Zoom *= MathF.Pow(1.015f, rightStickZoom * dt * 60f);

        var mouseWheelDelta = input.ConsumeMouseWheelDelta();
        if (mouseWheelDelta != 0)
            Zoom *= MathF.Pow(1.12f, mouseWheelDelta / 120.0f);

        Zoom = Math.Clamp(Zoom, 0.25f, 3.0f);

        if (WorldCenter != previousWorldCenter || Zoom != previousZoom)
            RebuildMatrices();
    }

    private void MoveInDirection(
        Vector2 direction,
        float speed,
        float dt,
        WorldCollisionResolver collision,
        bool noClipEnabled)
    {
        if (direction.LengthSquared() <= float.Epsilon)
            return;

        ApplyMovement(
            WorldCenter + Vector2.Normalize(direction) * speed * dt,
            collision,
            speed,
            dt,
            noClipEnabled);
    }

    private void MoveTowardTarget(
        Vector2 target,
        float speed,
        float dt,
        WorldCollisionResolver collision,
        bool noClipEnabled)
    {
        var delta = target - WorldCenter;
        var distance = delta.Length();
        if (distance <= float.Epsilon)
        {
            _movementTarget = null;
            CameraSpeedUnitVector = Vector2.Zero;
            return;
        }

        var step = speed * dt;
        if (step >= distance)
        {
            var movement = ApplyMovement(target, collision, speed, dt, noClipEnabled);
            if (movement.ReachedIntendedEnd || !movement.Moved)
                _movementTarget = null;
            return;
        }

        var stepMovement = ApplyMovement(
            WorldCenter + delta / distance * step,
            collision,
            speed,
            dt,
            noClipEnabled);
        if (!stepMovement.Moved)
            _movementTarget = null;
    }

    private MovementResult ApplyMovement(
        Vector2 intendedEnd,
        WorldCollisionResolver collision,
        float requestedSpeed,
        float dt,
        bool noClipEnabled)
    {
        var start = WorldCenter;
        var resolved = noClipEnabled
            ? intendedEnd
            : collision.ResolveMovement(start, intendedEnd);
        var actualDelta = resolved - start;
        WorldCenter = resolved;
        var actualDistance = actualDelta.Length();
        if (actualDistance > float.Epsilon)
        {
            CameraSpeedUnitVector = actualDelta / actualDistance;
            CharacterFacingUnitVector = CameraSpeedUnitVector;
            CurrentMovementSpeed = dt > float.Epsilon
                ? MathF.Min(requestedSpeed, actualDistance / dt)
                : requestedSpeed;
        }
        else
        {
            CameraSpeedUnitVector = Vector2.Zero;
        }

        return new MovementResult(
            actualDistance > float.Epsilon,
            Vector2.DistanceSquared(resolved, intendedEnd) <= 0.000001f);
    }

    private static Vector2 MovementDirection(
        InputState input,
        out float joystickMovementScale,
        out Vector2 joystickRotationOnly)
    {
        joystickMovementScale = 0.0f;
        joystickRotationOnly = Vector2.Zero;
        var joystick = new Vector2((float)input.LeftJoystickX, -(float)input.LeftJoystickY);
        var joystickLengthSquared = joystick.LengthSquared();
        if (joystickLengthSquared > JoystickDeadzone * JoystickDeadzone)
        {
            var joystickLength = MathF.Min(MathF.Sqrt(joystickLengthSquared), 1.0f);
            joystickMovementScale = ApplyMovementStickResponse(joystickLength);
            return Vector2.Transform(joystick / MathF.Sqrt(joystickLengthSquared), IsometricRotation);
        }
        if (joystickLengthSquared >= JoystickRotationDeadzone * JoystickRotationDeadzone)
            joystickRotationOnly = Vector2.Transform(joystick, IsometricRotation);
        
        var delta = Vector2.Zero;

        if (input.IsDown(VirtualKey.Left) || input.IsDown(VirtualKey.A))
        {
            delta.X -= 1;
            delta.Y += 1;
        }

        if (input.IsDown(VirtualKey.Right) || input.IsDown(VirtualKey.D))
        {
            delta.X += 1;
            delta.Y -= 1;
        }

        if (input.IsDown(VirtualKey.Up) || input.IsDown(VirtualKey.W))
        {
            delta.X -= 1;
            delta.Y -= 1;
        }

        if (input.IsDown(VirtualKey.Down) || input.IsDown(VirtualKey.S))
        {
            delta.X += 1;
            delta.Y += 1;
        }

        return delta;
    }

    private static float ApplyDeadzone(float value) =>
        MathF.Abs(value) < JoystickDeadzone ? 0.0f : value;

    private static float ApplyMovementStickResponse(float inputMagnitude)
    {
        var normalizedInput = Math.Clamp(
            (inputMagnitude - JoystickDeadzone) /
            (JoystickMaximumMovementInput - JoystickDeadzone),
            0.0f,
            1.0f);
        return JoystickAntiDeadzone + normalizedInput * (1.0f - JoystickAntiDeadzone);
    }

    private readonly record struct MovementResult(bool Moved, bool ReachedIntendedEnd);

    private void RebuildMatrices()
    {
        _viewportZoom = Zoom * _viewportHeight / _worldViewHeight;
        // Original Sacred style: no map rotation; pre-rendered terrain determines the perspective.
        var eye = new Vector3(WorldCenter.X, WorldCenter.Y - 650f, 650f);
        var target = new Vector3(WorldCenter.X, WorldCenter.Y, 0f);
        EyePosition = eye;
        View = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);

        // Preserve the original vertical game-world span. Deriving only the width from the current aspect
        // ratio prevents both stretching and vertical camera zoom when the window is resized.
        var halfH = _worldViewHeight * 0.5f / Zoom;
        var halfW = halfH * _viewportWidth / _viewportHeight;
        // Direct3D's viewport already maps positive clip-space Y toward the top of the screen.
        // Reversing the orthographic top and bottom here inverted all Z-up model geometry.
        Projection = Matrix4x4.CreateOrthographicOffCenter(-halfW, halfW, -halfH, halfH, 0.1f, 5000f);
    }
}
