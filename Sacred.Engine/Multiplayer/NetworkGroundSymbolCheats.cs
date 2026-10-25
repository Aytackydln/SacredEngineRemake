using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Runtime;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Multiplayer;

/// <summary>Reports the inputs and colours actually submitted by the live marker consumer.</summary>
internal static class NetworkGroundSymbolCheats
{
    public static bool TryExecute(string option, string value, NetworkScenePresentation view,
        AssetManager assets, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-ground-symbols", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith("hover ", StringComparison.OrdinalIgnoreCase))
        {
            var target = value[6..].Trim();
            if (target.Equals("live", StringComparison.OrdinalIgnoreCase))
            { view.DebugHealthHoverEnabled = false; message = "Health hover restored to the pointer."; }
            else if (target.Equals("off", StringComparison.OrdinalIgnoreCase) || uint.TryParse(target, out _))
            {
                view.DebugHealthHover = uint.TryParse(target, out var hovered) ? hovered : null;
                view.DebugHealthHoverEnabled = true;
                message = $"Health hover override: {view.DebugHealthHover?.ToString() ?? "off"}.";
            }
            else message = "Use set server-ground-symbols hover <reference|off|live>.";
            return true;
        }
        uint? selected = uint.TryParse(value, out var reference) ? reference : null;
        if (selected is null && !value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = "Use set server-ground-symbols <status|creature reference|hover reference|hover off|hover live>.";
            return true;
        }
        var world = view.World.Replicas;
        var hero = world.Objects.GetValueOrDefault(view.World.PlayerSlot);
        message = $"ground symbols: local hero {view.World.PlayerSlot}, level {hero?.Values?.Level}; " +
            $"difficulty {world.Session.Difficulty}, flags {world.Session.GameFlags?.ToString("X8") ?? "unknown"}; " +
            $"{view.GroundMarkers.Count} submitted markers";
        var references = selected is { } subject ? new[] { subject } : view.GroundMarkers.Select(marker => marker.Reference);
        foreach (var subjectReference in references)
        {
            if (!world.Objects.TryGetValue(subjectReference, out var actor) || actor.Values is not { } values)
            {
                EngineLog.WriteLine($"Ground symbol {subjectReference}: no creature values received.");
                continue;
            }
            var level = assets.CreatureBalance is { } balance
                ? SacredCreatureProgressionRules.BaseLevelForDifficulty(values.Level, world.Session.Difficulty, balance).ToString()
                : "unavailable";
            var marker = view.GroundMarkers.FirstOrDefault(entry => entry.Reference == subjectReference);
            EngineLog.WriteLine($"Ground symbol {subjectReference}: type {actor.BaseState.ObjectType}; " +
                $"level {values.Level}, base level {level}; HP {actor.CurrentHitPoints ?? values.CurrentHitPoints}/{values.MaximumHitPoints}; " +
                $"relation class {actor.Creature?.RelationClass}, viewer class {hero?.Creature?.RelationClass}; " +
                $"creature flags 0x{actor.Creature?.Word200:X8}; " +
                (marker is null ? "not submitted." :
                    $"health ARGB 0x{marker.HealthColor:X8}, border ARGB 0x{marker.BorderColor:X8}; " +
                    $"coverage {marker.HealthFraction:0.000}; health visible {marker.ShowHealth}; champion {marker.Champion}, elite {marker.Elite}."));
        }
        return true;
    }
}
