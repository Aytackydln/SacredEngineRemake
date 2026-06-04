using System.Numerics;
using Sacred.Engine.Platform;

namespace Sacred.Engine.Scene;

public sealed class ClickToMoveController
{
    private const float HoldClickThresholdSeconds = 0.15f;

    public static bool InstantlyStopAfterHoldClickMovement = true;

    private Vector2? _singleClickTarget;
    private float _heldSeconds;
    private bool _isHoldClickMovement;

    public void Update(
        InputState input,
        SacredCamera camera,
        int viewportWidth,
        int viewportHeight,
        float deltaSeconds)
    {
        if (input.TryConsumeLeftClick(out var clickPosition))
            BeginClick(camera, clickPosition, viewportWidth, viewportHeight);

        if (input.IsLeftMouseButtonDown && _singleClickTarget.HasValue)
            UpdateHeldClick(input, camera, viewportWidth, viewportHeight, deltaSeconds);

        if (input.ConsumeLeftMouseButtonReleased())
            EndClick(input, camera, viewportWidth, viewportHeight);
    }

    private void BeginClick(SacredCamera camera, Vector2 clickPosition, int viewportWidth, int viewportHeight)
    {
        _singleClickTarget = camera.ScreenToWorld(clickPosition, viewportWidth, viewportHeight);
        _heldSeconds = 0.0f;
        _isHoldClickMovement = false;
        camera.MoveTo(_singleClickTarget.Value);
    }

    private void UpdateHeldClick(
        InputState input,
        SacredCamera camera,
        int viewportWidth,
        int viewportHeight,
        float deltaSeconds)
    {
        _heldSeconds += deltaSeconds;
        if (_heldSeconds < HoldClickThresholdSeconds)
            return;

        _isHoldClickMovement = true;
        camera.MoveTo(camera.ScreenToWorld(input.MousePosition, viewportWidth, viewportHeight));
    }

    private void EndClick(InputState input, SacredCamera camera, int viewportWidth, int viewportHeight)
    {
        if (_singleClickTarget is not { } singleClickTarget)
            return;

        if (!_isHoldClickMovement)
        {
            camera.MoveTo(singleClickTarget);
        }
        else if (InstantlyStopAfterHoldClickMovement)
        {
            camera.StopMoving();
        }
        else
        {
            camera.MoveTo(camera.ScreenToWorld(input.MousePosition, viewportWidth, viewportHeight));
        }

        _singleClickTarget = null;
        _heldSeconds = 0.0f;
        _isHoldClickMovement = false;
    }
}
