using System;
using System.Linq;
using System.Numerics;
using Sacred.Core.World.Portals;
using Sacred.World.Portals;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Executes default-open OnMoveOver callbacks and disarms arrival cells until exited.</summary>
internal sealed class PortalTraversalController(WorldPortalScriptIndex portals)
{
    private Vector2? _previousPosition;
    private SacredPortalTrigger? _crossed;
    private SacredPortalTrigger[] _arrivalCells = [];
    public void Reset(Vector2 position)
    {
        _previousPosition = position;
        _crossed = null;
        _arrivalCells = [];
    }

    public bool IsPortalAt(Vector2 position, byte level) => portals.Triggers.Any(t => t.Contains(position, level));

    public void ObserveMovement(Vector2 start, Vector2 end, byte level)
    {
        if (start == end || _crossed is not null) return;
        foreach (var trigger in portals.Triggers)
            if (!_arrivalCells.Contains(trigger) && trigger.Intersects(start, end, level))
            {
                _crossed = trigger;
                return;
            }
    }

    public bool Update(SacredCamera camera, byte level, out byte destinationLevel)
    {
        destinationLevel = level;
        var current = camera.WorldCenter;
        if (_previousPosition is { } previous) ObserveMovement(previous, current, level);
        if (!_arrivalCells.Any(t => t.Contains(current, level))) _arrivalCells = [];
        _previousPosition = current;
        var trigger = _crossed;
        _crossed = null;
        if (trigger is null) return false;
        var target = trigger.Destination;
        // DefPos uses -1 to mean the outdoor/default surface.
        var targetLevel = (byte)(target.Z is >= 0 and <= 16 ? target.Z : 0);
        destinationLevel = targetLevel;
        var destination = new Vector2(target.X, target.Y);
        _arrivalCells = portals.Triggers.Where(t => t.Contains(destination, targetLevel)).ToArray();
        camera.StopMoving();
        camera.CenterOnTile(destination.X, destination.Y);
        _previousPosition = destination;
        EngineLog.WriteLine($"Script transition: {trigger.Name}; {current.X:0.##},{current.Y:0.##} level {level} -> {target.X},{target.Y} level {destinationLevel}.");
        return true;
    }
}
