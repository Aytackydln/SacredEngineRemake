using System;
using System.Numerics;
using Windows.Gaming.Input;
using Sacred.Engine.Platform;
using Sacred.World.Map;

namespace Sacred.Engine.Scene.WorldMap;

internal sealed class WorldMapInputController(
    InputState input,
    GamepadInputSource gamepad,
    WorldMapCamera camera,
    Action<Vector2> teleport,
    Action<GameSceneId> requestSwitch)
{
    private const float KeyboardPanSpeed = 650.0f;
    private const float GamepadDeadzone = 0.12f;
    private Vector2 _lastPointerPosition;
    private bool _mapDragging;
    private bool _mouseTargeting;

    public bool IsMinimapVisible { get; private set; }
    public bool IsControllerTargetVisible => input.UsingController;
    public Vector2 TargetWorldPosition { get; private set; }
    public Vector2 TargetScreenPosition { get; private set; }

    public bool Update(
        float deltaSeconds,
        int mapWidth,
        int mapHeight,
        int viewportWidth,
        int viewportHeight)
    {
        if (input.ConsumePressed(VirtualKey.M) ||
            input.ConsumePressed(VirtualKey.Escape) ||
            gamepad.WasPressed(GamepadButtons.View) ||
            gamepad.WasPressed(GamepadButtons.B))
        {
            Console.WriteLine("Returning from world map to game.");
            requestSwitch(GameSceneId.InGame);
            return false;
        }

        if (gamepad.WasPressed(GamepadButtons.A))
        {
            var target = TargetAtScreenCenter(mapWidth, viewportWidth, viewportHeight);
            TeleportTo(target, "controller A");
            return false;
        }

        var changed = UpdatePointer(mapWidth, mapHeight, viewportWidth, viewportHeight);
        var direction = KeyboardDirection() + GamepadDirection();
        if (direction.LengthSquared() > 1.0f)
            direction = Vector2.Normalize(direction);
        if (direction != Vector2.Zero)
        {
            changed |= camera.Pan(
                direction * KeyboardPanSpeed * Math.Max(0.0f, deltaSeconds) / camera.Zoom,
                mapWidth,
                mapHeight,
                viewportWidth,
                viewportHeight);
        }

        var zoomFactor = 1.0f;
        if (input.IsDown(VirtualKey.Q))
            zoomFactor /= MathF.Pow(1.8f, Math.Max(0.0f, deltaSeconds));
        if (input.IsDown(VirtualKey.E))
            zoomFactor *= MathF.Pow(1.8f, Math.Max(0.0f, deltaSeconds));

        var stickZoom = ApplyDeadzone((float)input.RightJoystickY);
        if (stickZoom != 0.0f)
            zoomFactor *= MathF.Pow(1.8f, stickZoom * Math.Max(0.0f, deltaSeconds));

        var wheelDelta = input.ConsumeMouseWheelDelta();
        if (wheelDelta != 0)
        {
            zoomFactor *= MathF.Pow(1.18f, wheelDelta / 120.0f);
            Console.WriteLine($"World map mouse-wheel zoom: {wheelDelta}.");
        }

        if (Math.Abs(zoomFactor - 1.0f) > float.Epsilon)
        {
            var anchor = wheelDelta != 0
                ? input.MousePosition
                : new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);
            changed |= camera.ChangeZoom(
                zoomFactor,
                anchor,
                mapWidth,
                mapHeight,
                viewportWidth,
                viewportHeight);
        }

        UpdateTargetState(mapWidth, viewportWidth, viewportHeight);
        return changed;
    }

    public void Reset()
    {
        _mapDragging = false;
        _mouseTargeting = false;
        IsMinimapVisible = false;
        input.DiscardPointerMovementEvents();
    }

    private bool UpdatePointer(int mapWidth, int mapHeight, int viewportWidth, int viewportHeight)
    {
        var changed = false;
        if (input.TryConsumeLeftClick(out var clickPosition) && input.IsLeftMouseButtonDown)
        {
            if (input.IsDown(VirtualKey.Control))
            {
                var target = TargetAt(clickPosition, mapWidth, viewportWidth, viewportHeight);
                TeleportTo(target, "Ctrl + left click");
            }
            else
            {
                _mouseTargeting = true;
                Console.WriteLine($"World map minimap target started at {clickPosition.X:0},{clickPosition.Y:0}.");
            }
        }

        if (input.ConsumeRightMouseButtonPressed() && input.IsRightMouseButtonDown)
        {
            _mapDragging = true;
            _lastPointerPosition = input.MousePosition;
            Console.WriteLine($"World map drag started at {_lastPointerPosition.X:0},{_lastPointerPosition.Y:0}.");
        }

        if (_mapDragging && input.IsRightMouseButtonDown)
        {
            var pointerPosition = input.MousePosition;
            var pointerDelta = pointerPosition - _lastPointerPosition;
            _lastPointerPosition = pointerPosition;
            if (pointerDelta != Vector2.Zero)
            {
                changed |= camera.Pan(
                    -pointerDelta / camera.Zoom,
                    mapWidth,
                    mapHeight,
                    viewportWidth,
                    viewportHeight);
            }
        }

        if (input.ConsumeRightMouseButtonReleased())
        {
            if (_mapDragging)
                Console.WriteLine("World map drag completed.");
            _mapDragging = false;
        }

        if (input.ConsumeLeftMouseButtonReleased())
        {
            if (_mouseTargeting)
                Console.WriteLine("World map minimap target closed.");
            _mouseTargeting = false;
        }

        return changed;
    }

    private void UpdateTargetState(int mapWidth, int viewportWidth, int viewportHeight)
    {
        var controllerPreview = gamepad.IsDown(GamepadButtons.X);
        IsMinimapVisible = controllerPreview || (_mouseTargeting && input.IsLeftMouseButtonDown);
        TargetScreenPosition = _mouseTargeting
            ? input.MousePosition
            : new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);
        TargetWorldPosition = TargetAt(TargetScreenPosition, mapWidth, viewportWidth, viewportHeight);
    }

    private Vector2 TargetAtScreenCenter(int mapWidth, int viewportWidth, int viewportHeight) =>
        TargetAt(
            new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f),
            mapWidth,
            viewportWidth,
            viewportHeight);

    private Vector2 TargetAt(Vector2 screenPosition, int mapWidth, int viewportWidth, int viewportHeight) =>
        WorldMapProjection.MapToWorld(
            camera.ScreenToMap(screenPosition, viewportWidth, viewportHeight),
            mapWidth);

    private void TeleportTo(Vector2 target, string source)
    {
        teleport(target);
        Console.WriteLine($"World map teleport ({source}) to {target.X:0.##}, {target.Y:0.##}.");
        requestSwitch(GameSceneId.InGame);
    }

    private Vector2 KeyboardDirection()
    {
        var x = 0.0f;
        var y = 0.0f;
        if (input.IsDown(VirtualKey.A) || input.IsDown(VirtualKey.Left)) x -= 1.0f;
        if (input.IsDown(VirtualKey.D) || input.IsDown(VirtualKey.Right)) x += 1.0f;
        if (input.IsDown(VirtualKey.W) || input.IsDown(VirtualKey.Up)) y -= 1.0f;
        if (input.IsDown(VirtualKey.S) || input.IsDown(VirtualKey.Down)) y += 1.0f;
        return new Vector2(x, y);
    }

    private Vector2 GamepadDirection()
    {
        var direction = new Vector2((float)input.LeftJoystickX, -(float)input.LeftJoystickY);
        return direction.LengthSquared() < GamepadDeadzone * GamepadDeadzone
            ? Vector2.Zero
            : direction;
    }

    private static float ApplyDeadzone(float value) =>
        Math.Abs(value) < GamepadDeadzone ? 0.0f : value;
}
