using System;
using System.Numerics;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene.InGame;

public sealed class SacredCamera
{
    private const float NormalMovementSpeed = 10.0f;
    private const float FastMovementSpeed = 30.0f;
    private const float JoystickDeadzone = 0.1f;

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
    private float _viewportZoom;
    private Vector2? _movementTarget;

    public Vector2 CameraSpeedUnitVector { get; private set; } = Vector2.Zero;

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

    public void StopMoving()
    {
        _movementTarget = null;
        CameraSpeedUnitVector = Vector2.Zero;
    }

    public void UpdateFromKeyboard(InputState input, float dt)
    {
        var previousWorldCenter = WorldCenter;
        var previousZoom = Zoom;
        var speed = (input.IsMoveFasterDown ? FastMovementSpeed : NormalMovementSpeed) / Zoom;
        var delta = MovementDirection(input);

        if (delta.LengthSquared() > 0)
        {
            _movementTarget = null;
            MoveInDirection(delta, speed, dt);
        }
        else if (_movementTarget is { } target)
        {
            MoveTowardTarget(target, speed, dt);
        }
        else
        {
            CameraSpeedUnitVector = Vector2.Zero;
        }

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

    private void MoveInDirection(Vector2 direction, float speed, float dt)
    {
        CameraSpeedUnitVector = Vector2.Normalize(direction);
        WorldCenter += CameraSpeedUnitVector * speed * dt;
    }

    private void MoveTowardTarget(Vector2 target, float speed, float dt)
    {
        var delta = target - WorldCenter;
        var distance = delta.Length();
        if (distance <= float.Epsilon)
        {
            _movementTarget = null;
            CameraSpeedUnitVector = Vector2.Zero;
            return;
        }

        CameraSpeedUnitVector = delta / distance;
        var step = speed * dt;
        if (step >= distance)
        {
            WorldCenter = target;
            _movementTarget = null;
            return;
        }

        WorldCenter += CameraSpeedUnitVector * step;
    }

    private static Vector2 MovementDirection(InputState input)
    {
        if (
            input.LeftJoystickX >= JoystickDeadzone || input.LeftJoystickX <= -JoystickDeadzone
                                                     || input.LeftJoystickY >= JoystickDeadzone || input.LeftJoystickY <= -JoystickDeadzone)
        {
            var movementDirection = new Vector2((float)input.LeftJoystickX, -(float)input.LeftJoystickY);
            
            // rotate for isometric camera:
            var rotatedDirection = Vector2.Transform(movementDirection, IsometricRotation);
            return rotatedDirection;
        }
        
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
