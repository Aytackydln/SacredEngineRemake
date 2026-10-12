using System;
using System.Globalization;
using Sacred.Engine.Assets;
using Sacred.UI.Character;

namespace Sacred.Engine.Scene.InGame;

internal static class ActorPortraitCheats
{
    public static bool TryExecute(string option, string value, ActorPortraitPanelState state, AssetManager assets, out string message)
    {
        message = string.Empty;
        if (option.Equals("health", StringComparison.OrdinalIgnoreCase) && state.Player is { } player &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) &&
            float.IsFinite(percent) && percent >= 0 && percent <= 100)
        { player.Character.Health.SetFraction(percent / 100); message = $"player HP {player.Character.Health.Fraction:P0}"; return true; }
        if (!option.Equals("portraits", StringComparison.OrdinalIgnoreCase) && !option.Equals("party", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = value.Split(':');
        switch (parts[0].ToLowerInvariant())
        {
            case "clear": state.Party.Clear(); break;
            case "add" when parts.Length == 2 && uint.TryParse(parts[1], out var entry) && entry >= 1 && entry <= assets.PlayerCharacterCount:
                if (state.Party.Members.Count >= ActorPortraitLayout.MaximumCompanions)
                { message = "native companion portrait column is full"; return true; }
                state.Party.Add(assets.CreatePlayerCharacterLoadout(entry).Actor); break;
            case "health" when parts.Length == 3 && int.TryParse(parts[1], out var index) &&
                (uint)index < (uint)state.Party.Members.Count && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var hp) &&
                float.IsFinite(hp) && hp >= 0 && hp <= 100:
                state.Party.Members[index].Character.Health.SetFraction(hp / 100); break;
            case "status": break;
            default: message = "portraits/party: status, clear, add:<character-index>, health:<member-index>:<percent>; set health <0..100>"; return true;
        }
        message = $"portraits {(state.Layout is null ? "loading" : "ready")}; player {state.Player?.CharacterClass}; " +
            $"HP {state.Player?.Character.Health.Fraction:P0}; party {state.Party.Members.Count}";
        for (var index = 0; index < state.Party.Members.Count; index++)
            EngineLog.WriteLine($"Party portrait {index}: {state.Party.Members[index].CharacterClass}; HP {state.Party.Members[index].Character.Health.Fraction:P0}.");
        return true;
    }
}
