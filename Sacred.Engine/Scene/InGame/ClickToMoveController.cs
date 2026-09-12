using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

public sealed class ClickToMoveController
{
    private const float HoldClickThresholdSeconds = 0.15f;
    private const float RotationOnlyRadius = 0.4f;
    private const float WaypointArrivalRadius = 0.03f;
    private const float RetargetScreenDistancePixels = 6.0f;
    private const float ControllerRotationDeadzone = 0.075f;

    public static bool InstantlyStopAfterHoldClickMovement = true;

    private Vector2? _singleClickTarget;
    private readonly Queue<Vector2> _route = new();
    private Vector2? _routeTarget;
    private float _heldSeconds;
    private bool _isHoldClickMovement;

    public void StopMoving()
    {
        _singleClickTarget = null;
        _route.Clear();
        _routeTarget = null;
        _heldSeconds = 0.0f;
        _isHoldClickMovement = false;
    }

    public void Update(
        InputState input,
        SacredCamera camera,
        int viewportWidth,
        int viewportHeight,
        Func<Vector2, Vector2> outputToViewport,
        WorldCollisionResolver collision,
        WorldElevationSampler elevation,
        CollisionCheatMode collisionMode,
        Func<Vector2, bool> tryInteractAt,
        float deltaSeconds)
    {
        if (HasManualMovementIntent(input))
        {
            StopMoving();
            camera.StopMoving();
            return;
        }

        AdvanceRoute(camera);

        if (input.TryConsumeLeftClick(out var clickPosition))
        {
            var worldTarget = GameActorElevation.ScreenToWorldOnSurface(
                camera, elevation, outputToViewport(clickPosition), viewportWidth, viewportHeight);
            if (tryInteractAt(worldTarget))
            {
                StopMoving();
                camera.StopMoving();
            }
            else
            {
                BeginClick(camera, collision, worldTarget, collisionMode);
            }
        }

        if (input.IsLeftMouseButtonDown && _singleClickTarget.HasValue)
            UpdateHeldClick(input, camera, collision, elevation, viewportWidth, viewportHeight, outputToViewport, collisionMode, deltaSeconds);

        if (input.ConsumeLeftMouseButtonReleased())
            EndClick(input, camera, collision, elevation, viewportWidth, viewportHeight, outputToViewport, collisionMode);
    }

    private void BeginClick(
        SacredCamera camera,
        WorldCollisionResolver collision,
        Vector2 worldTarget,
        CollisionCheatMode collisionMode)
    {
        _singleClickTarget = worldTarget;
        _heldSeconds = 0.0f;
        _isHoldClickMovement = false;
        SetRouteTo(camera, collision, _singleClickTarget.Value, collisionMode);
    }

    private void UpdateHeldClick(
        InputState input,
        SacredCamera camera,
        WorldCollisionResolver collision,
        WorldElevationSampler elevation,
        int viewportWidth,
        int viewportHeight,
        Func<Vector2, Vector2> outputToViewport,
        CollisionCheatMode collisionMode,
        float deltaSeconds)
    {
        _heldSeconds += deltaSeconds;
        if (_heldSeconds < HoldClickThresholdSeconds)
            return;

        _isHoldClickMovement = true;
        var target = GameActorElevation.ScreenToWorldOnSurface(
            camera, elevation, outputToViewport(input.MousePosition), viewportWidth, viewportHeight);
        if (ShouldRetarget(camera, target))
            SetRouteTo(camera, collision, target, collisionMode);
    }

    private void EndClick(
        InputState input,
        SacredCamera camera,
        WorldCollisionResolver collision,
        WorldElevationSampler elevation,
        int viewportWidth,
        int viewportHeight,
        Func<Vector2, Vector2> outputToViewport,
        CollisionCheatMode collisionMode)
    {
        if (!_singleClickTarget.HasValue)
            return;

        // A normal click starts its route on button-down and keeps it on release. Held
        // movement either stops immediately or keeps following the final pointer location.
        if (_isHoldClickMovement)
        {
            if (InstantlyStopAfterHoldClickMovement)
            {
                StopMoving();
                camera.StopMoving();
            }
            else
            {
                SetRouteTo(camera, collision, GameActorElevation.ScreenToWorldOnSurface(
                    camera, elevation, outputToViewport(input.MousePosition), viewportWidth, viewportHeight), collisionMode);
            }
        }

        _singleClickTarget = null;
        _heldSeconds = 0.0f;
        _isHoldClickMovement = false;
    }

    private void SetRouteTo(
        SacredCamera camera,
        WorldCollisionResolver collision,
        Vector2 target,
        CollisionCheatMode collisionMode)
    {
        var direction = target - camera.WorldCenter;
        if (direction.LengthSquared() <= RotationOnlyRadius * RotationOnlyRadius)
        {
            _route.Clear();
            _routeTarget = null;
            camera.StopMoving();
            camera.RotateToward(direction);
            return;
        }

        if (collisionMode is CollisionCheatMode.Fly or CollisionCheatMode.NoClip)
        {
            _route.Clear();
            _routeTarget = target;
            camera.MoveTo(target);
            return;
        }

        var pathFinder = new WorldClickPathFinder(collision);
        if (!pathFinder.TryFindRoute(camera.WorldCenter, target, camera.Zoom, out var route))
        {
            StopMoving();
            camera.StopMoving();
            camera.RotateToward(direction);
            return;
        }

        _route.Clear();
        foreach (var waypoint in route)
            _route.Enqueue(waypoint);
        _routeTarget = target;
        AdvanceRoute(camera);
    }

    private void AdvanceRoute(SacredCamera camera)
    {
        while (_route.TryPeek(out var waypoint) &&
               Vector2.DistanceSquared(camera.WorldCenter, waypoint) <= WaypointArrivalRadius * WaypointArrivalRadius)
        {
            _route.Dequeue();
        }

        if (_route.TryPeek(out var nextWaypoint))
        {
            camera.MoveTo(nextWaypoint);
            return;
        }

        _routeTarget = null;
    }

    private bool ShouldRetarget(SacredCamera camera, Vector2 target)
    {
        if (_routeTarget is not { } previousTarget)
            return true;

        // Six screen pixels is the smallest meaningful retargeting distance. At lower zoom
        // that naturally becomes a larger world-space distance, avoiding repeated A* work.
        var worldDistance = RetargetScreenDistancePixels /
            (IsometricProjection.StepHeight * MathF.Max(camera.ViewportZoom, float.Epsilon));
        return Vector2.DistanceSquared(previousTarget, target) >= worldDistance * worldDistance;
    }

    private static bool HasManualMovementIntent(InputState input)
    {
        var stick = new Vector2((float)input.LeftJoystickX, (float)input.LeftJoystickY);
        return stick.LengthSquared() >= ControllerRotationDeadzone * ControllerRotationDeadzone ||
               input.IsDown(VirtualKey.Left) || input.IsDown(VirtualKey.A) ||
               input.IsDown(VirtualKey.Right) || input.IsDown(VirtualKey.D) ||
               input.IsDown(VirtualKey.Up) || input.IsDown(VirtualKey.W) ||
               input.IsDown(VirtualKey.Down) || input.IsDown(VirtualKey.S);
    }

}
