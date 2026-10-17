using System;
using System.Collections.Generic;
using System.IO;
using Sacred.Core.Pax.Hero;
using Sacred.Engine.Graphics.ImGui;

namespace Sacred.Engine.Multiplayer;

/// <summary>Read-only choices from the native saved hero slots; invalid slots remain visible.</summary>
internal static class GameServerSavedHeroCatalog
{
    public static IReadOnlyList<GameServerSavedHeroChoice> Read(string directory)
    {
        var choices = new List<GameServerSavedHeroChoice>();
        for (var selection = 1; selection <= 8; selection++)
        {
            try
            {
                var hero = SacredHeroPaxProfile.ReadSelected(directory, selection);
                choices.Add(new(selection, $"{selection}: {hero.Name} (level {hero.Level})"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                choices.Add(new(selection, $"{selection}: unavailable", error.Message));
            }
        }
        return choices;
    }
}
