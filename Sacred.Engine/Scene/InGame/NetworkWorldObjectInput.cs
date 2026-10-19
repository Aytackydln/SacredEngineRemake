using System;
using System.Linq;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Mouse, keyboard and controller interactions use received models, footprint reach and navigation.</summary>
internal sealed class NetworkWorldObjectInput(DoorSceneController objects, SacredCamera camera,
    ClickToMoveController movement, WorldCollisionResolver collision)
{
    private WorldFocusTarget? _hover;
    private WorldFocusTarget? _pending;
    private long _retryAt;
    public bool HasHover => _hover.HasValue;
    public string Status => _pending is { } pending ? $"approaching object {pending.Id}" : objects.NetworkInteractionStatus;

    public void Update(InputState input, Vector2 pointer, int width, int height, MovementInputAvailability availability,
        bool allowInteraction, bool controllerInteractionPressed)
    {
        ClearHover();
        if (!allowInteraction) return;
        if (camera.GetManualDirection(input, availability).LengthSquared() > 0.000001f) Cancel();
        if (!input.UiWantsMouse)
        {
            var best = float.PositiveInfinity;
            foreach (var target in objects.FocusTargets)
                if (WorldModelFocusPicker.TryScore(target.Model!, camera, pointer, width, height, out var score) && score < best)
                { _hover = target; best = score; }
        }
        if (_pending is { } pending)
        {
            var current = objects.FocusTargets.FirstOrDefault(target => target.Id == pending.Id);
            if (current.Model is null || !ReferenceEquals(current.Model, pending.Model))
            {
                EngineLog.WriteLine($"Network interaction cancelled: received object {pending.Id} was removed or replaced.");
                Cancel();
            }
            else if (WorldInteractionApproach.InReach(current, camera.WorldCenter))
            {
                Cancel();
                camera.RotateToward(current.Position - camera.WorldCenter);
                objects.TryToggleFocused(current.Id);
            }
            else if (!movement.HasRoute && Environment.TickCount64 >= _retryAt)
            {
                // Authoritative corrections and sector initialization can retire a route.
                // Keep the selected received instance and find a fresh route from here.
                if (WorldInteractionApproach.TryFind(current, camera, collision, out var destination))
                    movement.NavigateTo(camera, collision, destination, CollisionCheatMode.Walk);
                else EngineLog.WriteLine($"Network interaction waits for a reachable position for object {pending.Id}.");
                _retryAt = Environment.TickCount64 + 500;
            }
        }
        if (_hover?.Model is { } hovered) hovered.IsFocused = true;
        if ((!input.UiWantsKeyboard && input.ConsumePressed(VirtualKey.F)) || controllerInteractionPressed)
        {
            var target = _hover ?? objects.FocusTargets
                .Where(target => Vector2.DistanceSquared(target.Position, camera.WorldCenter) <= WorldFocusController.Range * WorldFocusController.Range)
                .OrderBy(target => Vector2.DistanceSquared(target.Position, camera.WorldCenter))
                .Select(target => (WorldFocusTarget?)target).FirstOrDefault();
            if (target is { } selected) Request(selected);
        }
    }

    public bool Click()
    {
        if (_hover is { } target) return Request(target);
        CancelApproach();
        return false;
    }
    public void ClearHover() { if (_hover?.Model is { } model) model.IsFocused = false; _hover = null; }
    public void CancelApproach()
    {
        if (_pending is { } pending) EngineLog.WriteLine($"Network interaction cancelled: approach to object {pending.Id} stopped.");
        _pending = null; ClearHover();
    }
    public bool Request(uint reference)
    {
        var target = objects.FocusTargets.FirstOrDefault(target => target.Id == reference);
        if (target.Model is null) throw new InvalidOperationException("The received object is not currently available for interaction.");
        return Request(target);
    }
    private bool Request(WorldFocusTarget target)
    {
        Cancel();
        if (WorldInteractionApproach.InReach(target, camera.WorldCenter))
        {
            camera.RotateToward(target.Position - camera.WorldCenter);
            return objects.TryToggleFocused(target.Id);
        }
        if (WorldInteractionApproach.TryFind(target, camera, collision, out var destination))
        {
            _pending = target;
            _retryAt = 0;
            movement.NavigateTo(camera, collision, destination, CollisionCheatMode.Walk);
            EngineLog.WriteLine($"Network interaction approach: object {target.Id}, destination {destination}.");
        }
        else EngineLog.WriteLine($"Network interaction unavailable: no reachable position for object {target.Id}.");
        return true;
    }
    private void Cancel() { _pending = null; movement.StopMoving(); camera.StopMoving(); }
}
