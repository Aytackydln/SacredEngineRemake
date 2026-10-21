using System;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;

namespace Sacred.Engine.Multiplayer;

internal static class NetworkStatusEffectCheats
{
    public static bool TryExecute(string option, string value, IGameplayObjectWorld world, out string message)
    {
        message = string.Empty;
        if (!option.Equals("status-fx", StringComparison.OrdinalIgnoreCase)) return false;
        if (world.StatusEffects is not { } effects)
        {
            message = "Status FX lifecycle is unavailable for this world.";
            return true;
        }
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words is ["clear"])
        {
            effects.Clear();
            message = "Status FX cleared.";
        }
        else if (words is ["status"])
        {
            foreach (var fx in effects.Entries.Values)
                EngineLog.WriteLine($"Status FX {fx.Generation}: {fx.Definition.Kind} {fx.Definition.Code}; caster {fx.Caster}; owner {fx.Owner}; resource {fx.Type}; bone {fx.Bone}; age {fx.Age:F2}/{fx.Duration}; {fx.PendingReason ?? "visual ready"}.");
            message = $"Status FX: {effects.Entries.Count} channels; {effects.PendingActions} pending actions.";
        }
        else if (words.Length is >= 4 and <= 5 &&
                 Enum.TryParse<SacredCombatArtKind>(words[0], true, out var kind) &&
                 uint.TryParse(words[1], out var code) && uint.TryParse(words[2], out var caster) &&
                 uint.TryParse(words[3], out var target) &&
                 (words.Length == 4 || int.TryParse(words[4], out _)))
        {
            var rank = words.Length == 5 ? int.Parse(words[4]) : 1;
            message = rank >= 0 && effects.Apply(caster, target, kind, code, rank)
                ? "Status FX factory applied; see status-fx status for channels and pending work."
                : "No decoded factory or active caster for this request.";
        }
        else message = "Usage: set status-fx status|clear|Spell <code> <caster> <target> [rank]|SpecialMove <code> <caster> <target> [rank]";
        return true;
    }
}
