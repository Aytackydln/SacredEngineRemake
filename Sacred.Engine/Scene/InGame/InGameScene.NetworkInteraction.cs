using System;
using System.Linq;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private bool TrySetNetworkInteraction(string option, string value, out string message)
    {
        message = string.Empty;
        if (option.Equals("server-fx", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (fxReference, reason) in _particles.ObjectEffects.Pending)
                EngineLog.WriteLine($"Network FX pending: ref {fxReference}; {reason}");
            foreach (var (generation, reason) in _particles.CreatureEffects.Pending)
                EngineLog.WriteLine($"Creature FX pending: generation {generation}; {reason}");
            foreach (var (order, reason) in _particles.ReceivedEffects.Pending)
                EngineLog.WriteLine($"Received FX creation pending: work {order}; {reason}");
            message = $"{_particles.ObjectEffects.ActiveCount} active received FX; {_particles.ObjectEffects.Pending.Count} waiting for decoded inputs; " +
                $"{_particles.CreatureEffects.ActiveCount} active creature FX; {_particles.CreatureEffects.Pending.Count} pending creature FX; " +
                $"{_particles.ReceivedEffects.ActiveCount} active client-local creations; {_particles.ReceivedEffects.StartedCount} started; {_particles.ReceivedEffects.Pending.Count} pending creations; " +
                $"combat flashes {_particles.CombatEffects.ActiveCount} active/{_particles.CombatEffects.StartedCount} started; " +
                $"blood bursts {_particles.BloodEffects.ActiveCount} active/{_particles.BloodEffects.StartedCount} started; " +
                $"deflection bursts {_particles.DeflectionEffects.ActiveCount} active/{_particles.DeflectionEffects.StartedCount} started; " +
                $"creature ground markers {_particles.CreatureMarkers.Markers.Count}";
            return true;
        }
        if (option.Equals("server-drops", StringComparison.OrdinalIgnoreCase))
        {
            var objects = _networkPresentation?.GameplayObjects.Objects.Values.Where(obj => obj.Drop is not null).ToArray() ?? [];
            foreach (var obj in objects)
                EngineLog.WriteLine($"Network drop: ref {obj.Reference}, resource {obj.Placement.TypeId}, tile {obj.Drop!.Value.Position}, height {obj.Drop.Value.Height:0.###}, flying {obj.Drop.Value.IsFlying}, pickable {obj.Drop.Value.IsPickable}.");
            message = $"{objects.Length} received ground items; {_particles.ObjectSparkles.ActiveCount} with world sparkles; {_doors.NetworkInteractionStatus}";
            return true;
        }
        if (!option.Equals("server-interact", StringComparison.OrdinalIgnoreCase)) return false;
        if (_networkMovement is null || !NetworkWorldReady)
        { message = "Network interactions wait for a bound participant world and model."; return true; }
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        { message = _networkMovement.InteractionStatus; return true; }
        if (!uint.TryParse(value, out var reference))
        { message = "Use set server-interact <runtime object reference|status>."; return true; }
        try
        {
            _networkMovement.Interact(reference);
            message = _networkMovement.InteractionStatus;
        }
        catch (Exception error) { message = "Network interaction: " + error.Message; }
        return true;
    }
}
