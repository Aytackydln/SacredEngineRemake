using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private readonly PlayerCombatActions _combatActions;

    internal bool SetPlayerLevel(int level)
    {
        if (_networkPresentation is not null || _scene.EquipmentUi.Actor is not { } actor || level is < 1 or > ushort.MaxValue) return false;
        actor.Progression.SetLevel((ushort)level);
        EngineLog.WriteLine($"Debug input: player level set to {level}; {_scene.EquipmentUi.Actor.CharacterClass}.");
        return true;
    }

    internal bool SetAllCombatArtsLevelOne()
    {
        if (_networkPresentation is not null || _scene.EquipmentUi.Actor is not { } actor) return false;
        var codes = _scene.EquipmentUi.CombatArts.Available.Select(art => art.Code).ToArray();
        foreach (var code in codes) actor.Progression.SetCombatArt(code, 1);
        EngineLog.WriteLine($"Debug input: all {codes.Length} {_scene.EquipmentUi.Actor.CharacterClass} combat arts set to base level 1.");
        return true;
    }

    private bool RequestPlayerCombat(PlayerCombatRequest request, ushort? code = null)
    {
        if (_networkPresentation is not null) return false;
        var slots = _scene.EquipmentUi.CombatArts.QuickSlots;
        var selected = _scene.Hud!.SelectedCombatArtSlot;
        var visibleSlots = _scene.Hud.Layout?.QuickSlots.CombatArtSlots.Count ?? 0;
        code ??= (uint)selected < (uint)slots.Count && selected < visibleSlots ? slots[selected] : null;
        code ??= _scene.EquipmentUi.CombatArts.SelectedCode;
        var accepted = request.Cast ? _combatActions.Cast(_player.CombatContext, code, request.SelfTarget, request.Target)
            : _combatActions.Attack(_player.CombatContext);
        if (!accepted) return false;
        _inputController.SuspendGameplayInput();
        var facing = request.Target - _camera.WorldCenter;
        if (facing.LengthSquared() > .000001f) _camera.RotateToward(Vector2.Normalize(facing));
        return true;
    }

    private bool TrySetCombatOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (option.ToLowerInvariant() is not ("player-level" or "combat-arts" or "cast" or "attack")) return false;
        if (_networkPresentation is not null)
        { message = "Local combat testing and progression edits are available after disconnecting from GameServer."; return true; }
        switch (option.ToLowerInvariant())
        {
            case "player-level":
                message = int.TryParse(value, out var level) && SetPlayerLevel(level)
                    ? $"player level {level}" : $"player level {_scene.EquipmentUi.Actor?.Progression.Level}; use player-level <1..65535>";
                break;
            case "combat-arts":
                message = value.Equals("all1", StringComparison.OrdinalIgnoreCase) && SetAllCombatArtsLevelOne()
                    ? "all class combat arts set to base level 1"
                    : $"learned {_scene.EquipmentUi.Actor?.CombatArts.Entries.Count}; use combat-arts all1";
                break;
            case "cast" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                message = _combatActions.Diagnostics; break;
            case "cast" when value.Equals("clear", StringComparison.OrdinalIgnoreCase):
                _combatActions.Reset(); message = "local combat motion and effects cleared"; break;
            case "cast" when value.Equals("stop", StringComparison.OrdinalIgnoreCase):
                _combatActions.StopMotion(); message = "local combat motion stopped"; break;
            case "cast":
                var parts = value.Split('@');
                ushort? code = parts[0].Equals("selected", StringComparison.OrdinalIgnoreCase) ? null :
                    ushort.TryParse(parts[0], out var parsed) ? parsed : null;
                var self = parts.Length == 2 && parts[1].Equals("self", StringComparison.OrdinalIgnoreCase);
                var target = _camera.WorldCenter + _camera.CharacterFacingUnitVector;
                if (parts.Length == 2 && !self)
                {
                    var xy = parts[1].Split(',');
                    if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                        !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !float.IsFinite(x) || !float.IsFinite(y))
                    { message = "cast: <learned-code|selected>[@self|@x,y], status, stop or clear"; return true; }
                    target = new(x, y);
                }
                if (parts.Length > 2 || code is null && !parts[0].Equals("selected", StringComparison.OrdinalIgnoreCase))
                { message = "cast: <learned-code|selected>[@self|@x,y], status, stop or clear"; return true; }
                RequestPlayerCombat(new(true, self ? _camera.WorldCenter : target, self), code);
                message = _combatActions.Status;
                break;
            case "attack":
                RequestPlayerCombat(new(false, _camera.WorldCenter + _camera.CharacterFacingUnitVector));
                message = _combatActions.Status;
                break;
        }
        return true;
    }
}
