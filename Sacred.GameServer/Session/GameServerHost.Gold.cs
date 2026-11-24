using Sacred.GameServer.Scripting;

namespace Sacred.GameServer.Session;

/// <summary>Script rewards: gold piles (<see cref="GoldPile"/>) dropped next to the player.</summary>
public sealed partial class GameServerHost
{
    /// <summary>
    /// Drops a pile of <paramref name="amount"/> gold at the hero's position: an item object of the money resource the game uses
    /// for that amount, its value block carrying the amount as price, with a server reference owned by the player and sent to
    /// every in-game player as 0x16F (like <see cref="IScriptServer.DropItem"/>). Picking it up adds the amount to the hero's gold
    /// on the picking client (Gold 0x558304 -> AddGold 0x57FD80). Needs no game data.
    /// </summary>
    ScriptDrop? IScriptServer.DropGold(int slot, uint amount, out string? error)
    {
        error = null;
        if (amount is 0 or > GoldPile.MaximumAmount) { error = $"amount must be 1..{GoldPile.MaximumAmount}"; return null; }
        if (PlayerAt(slot) is not { } player) { error = "no such player"; return null; }
        if (!_world.TryGet(player.Slot, out var hero)) { error = "the player's hero is not in the world yet"; return null; }
        var type = GoldPile.ResourceFor(amount);
        var reference = DropNextToHero(player, hero, type, GoldPile.Values(amount), weaponExtension: false, out error);
        if (reference == 0) return null;
        Log.Info($"SCRIPT: dropped {amount} gold (type 0x{type:X}, ref {reference}) for #{player.Slot} {player.Name}.");
        return new ScriptDrop(reference, type, 0, "Gold", "Gold");
    }
}
