using System;
using System.Numerics;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene;

public sealed class SacredCamera
{
    private static readonly Matrix3x2 IsometricRotation = Matrix3x2.CreateRotation(-MathF.PI / 4);

    public Vector2 WorldCenter { get; private set; }
    public float Zoom { get; private set; } = 1.0f;
    public Vector3 EyePosition { get; private set; }
    public Matrix4x4 View { get; private set; }
    public Matrix4x4 Projection { get; private set; }

    private readonly int _width;
    private readonly int _height;

    public Vector2 CameraSpeedUnitVector { get; private set; } = Vector2.Zero;

    private SacredCamera(int width, int height)
    {
        _width = width;
        _height = height;
        RebuildMatrices();
    }

    public static SacredCamera CreateDefault(int width, int height) => new(width, height);

    public void CenterOnTile(float x, float y, float? zoom = null)
    {
        WorldCenter = new Vector2(x, y);
        if (zoom is { } value)
            Zoom = Math.Clamp(value, 0.25f, 3.0f);
        RebuildMatrices();
    }

    public void UpdateFromKeyboard(InputState input, float dt)
    {
        var speed = (input.IsDown(VirtualKey.Shift) ? 30f : 10f) / Zoom;
        var delta = MovementDirection(input);

        if (delta.LengthSquared() > 0)
        {
            CameraSpeedUnitVector = Vector2.Normalize(delta);
            delta = CameraSpeedUnitVector * speed * dt;
        }
        else
        {
            CameraSpeedUnitVector = Vector2.Zero;
        }

        WorldCenter += delta;
        if (input.IsDown(VirtualKey.Q)) Zoom *= MathF.Pow(0.985f, dt * 60f);
        if (input.IsDown(VirtualKey.E)) Zoom *= MathF.Pow(1.015f, dt * 60f);
        Zoom = Math.Clamp(Zoom, 0.25f, 3.0f);

        RebuildMatrices();
    }

    private static Vector2 MovementDirection(InputState input)
    {
        if (
            input.LeftJoystickX >= 0.1 || input.LeftJoystickX <= -0.1
                                       || input.LeftJoystickY >= 0.1 || input.LeftJoystickY <= -0.1)
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

    private void RebuildMatrices()
    {
        // Original Sacred style: no map rotation; pre-rendered terrain determines the perspective.
        var eye = new Vector3(WorldCenter.X, WorldCenter.Y - 650f, 650f);
        var target = new Vector3(WorldCenter.X, WorldCenter.Y, 0f);
        EyePosition = eye;
        View = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);

        var halfW = _width * 0.5f / Zoom;
        var halfH = _height * 0.5f / Zoom;
        Projection = Matrix4x4.CreateOrthographicOffCenter(-halfW, halfW, halfH, -halfH, 0.1f, 5000f);
    }
}
