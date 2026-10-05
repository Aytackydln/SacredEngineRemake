using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Keeps a clicked target until reachable; hovering another object never redirects it.</summary>
internal sealed class WorldInteractionController(DoorSceneController objects, StairsTraversalController stairs,
    SacredCamera camera, ClickToMoveController movement, WorldCollisionResolver collision)
{
    private WorldFocusTarget? _pending;
    private byte _surfaceLevel;

    public void Cancel() => _pending = null;

    public bool Request(WorldFocusTarget? target, byte surfaceLevel, CollisionCheatMode mode)
    {
        Cancel();
        movement.StopMoving();
        camera.StopMoving();
        if (target is not { } selected) return false;
        if (TryActivate(selected, surfaceLevel)) return true;
        if (WorldInteractionApproach.InReach(selected, camera.WorldCenter)) return true;
        if (!WorldInteractionApproach.TryFind(selected, camera, collision, out var destination))
        {
            EngineLog.WriteLine($"Interaction unavailable: {selected.Kind} {selected.Id}; no reachable position in range.");
            return true;
        }
        _pending = selected;
        _surfaceLevel = surfaceLevel;
        movement.NavigateTo(camera, collision, destination, mode);
        EngineLog.WriteLine($"Interaction approach: {selected.Kind} {selected.Id}; destination {destination}; reach {(selected.Model is null ? 1.5f : WorldObjectInteractionReach.Distance)} tiles from footprint.");
        return true;
    }

    public void Update(byte surfaceLevel)
    {
        if (_pending is not { } selected) return;
        if (surfaceLevel != _surfaceLevel || selected.Model is not null && !objects.HasFocusTarget(selected.Id))
        { Cancel(); movement.StopMoving(); camera.StopMoving(); return; }
        if (!WorldInteractionApproach.InReach(selected, camera.WorldCenter)) return;
        Cancel();
        movement.StopMoving();
        camera.StopMoving();
        TryActivate(selected, surfaceLevel);
    }

    private bool TryActivate(WorldFocusTarget target, byte surfaceLevel)
    {
        if (!WorldInteractionApproach.InReach(target, camera.WorldCenter)) return false;
        camera.RotateToward(target.Position - camera.WorldCenter);
        return target.Stairs is not null
            ? stairs.TryActivate(target, camera.WorldCenter, surfaceLevel)
            : objects.TryToggleFocused(target.Id);
    }
}
