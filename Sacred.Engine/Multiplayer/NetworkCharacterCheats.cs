using System;
using Sacred.Engine.Scene;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Inspect received state through the existing console without editing an offline loadout.</summary>
internal static class NetworkCharacterCheats
{
    public static bool TryExecute(string option, string value, SceneState scene, GoldWorldReplica world, out string message)
    {
        message = string.Empty;
        switch (option.ToLowerInvariant())
        {
            case "server-combat":
                var combat = world.Combat;
                message = $"received combat: {combat.Attacks} attacks; {combat.DamageEvents} damage events; {combat.HealthUpdates} absolute HP updates, {combat.HealingUpdates} increases; history {combat.Recent.Count}; version {combat.Version}";
                return true;
            case "equipment" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                if (scene.EquipmentUi.Actor is { } actor)
                    for (var index = 0; index < actor.EquipmentSlots.Count; index++)
                    {
                        var slot = actor.EquipmentSlots[index];
                        EngineLog.WriteLine($"Network equipment slot {index}: {slot.Type}; {slot.Equipment?.Name ?? "empty"}; native instance {slot.Instance?.NativeValues is not null}; item level {slot.Instance?.Level.ToString() ?? "none"}.");
                    }
                message = $"received local equipment listed; player {world.LocalPlayerSlot}";
                return true;
            case "network-character":
                var calendar = world.Calendar.Current;
                var local = scene.EquipmentUi.Actor;
                message = $"network character {world.LocalPlayerSlot}; UI {local?.CharacterClass.ToString() ?? "loading"}; level {local?.Progression.Level}; HP {local?.Character.Health.CurrentHitPoints}/{local?.Character.Health.MaximumHitPoints}; gold {local?.Progression.Gold}; " +
                    $"calendar {(calendar is null ? "awaiting native snapshot" : $"{calendar.Year}/{calendar.Month}/{calendar.Day} {world.Calendar.DayFraction * 24:0.00}h")}; calendar revision {world.Calendar.Revision}";
                if (local?.Movement is { } speed)
                    message += $"; movement {speed.Percent}%; walk/run {speed.Walk}/{speed.Run} native units/s ({speed.WalkTilesPerSecond:0.###}/{speed.RunTilesPerSecond:0.###} tiles/s)";
                return true;
            case "equip" or "unequip" or "equip-set" or "equipment" or "character" or "health" or "party":
                message = "This offline debug edit is unavailable while the received network character is bound.";
                return true;
            default: return false;
        }
    }
}
