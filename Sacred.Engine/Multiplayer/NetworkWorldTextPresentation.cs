using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.GameRes;
using Sacred.Engine.Scene;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Gold's global 32-slot text readouts. Resolves original text resources without requiring an actor model.</summary>
internal sealed class NetworkWorldTextPresentation
{
    private readonly GameplayWorldText?[] _entries = new GameplayWorldText[32];
    private long _version;
    private long _session = -1;

    public void Update(GoldWorldReplica world, GameResStore resources, SceneState scene, Func<Vector2, float> height, float seconds)
    {
        if (_session != world.SessionVersion) { Array.Clear(_entries); _version = 0; _session = world.SessionVersion; }
        for (var index = 0; index < _entries.Length; index++)
        {
            _entries[index]?.Advance(seconds);
            if (_entries[index] is { Expired: true }) _entries[index] = null;
        }
        foreach (var notification in world.Combat.Recent)
        {
            if (notification.Version <= _version || notification.Event is not GoldCreatureTextEvent { ReceiptPosition: { } position } text) continue;
            var slot = Array.FindIndex(_entries, entry => entry is null);
            if (slot < 0)
            {
                // Gold 0x6191A4 permits selector 0x42F to reuse a numeric (resource-zero) slot when full.
                slot = text.TextId == 0x42F ? Array.FindIndex(_entries, entry => entry?.TextId is 0 or uint.MaxValue) : -1;
                if (slot < 0) continue;
            }
            _entries[slot] = new(text.TextId, position.X, position.Y, GoldCreatureTextLayout.ColorArgb);
        }
        _version = world.Combat.Version;
        scene.CombatText.WorldText = _entries.OfType<GameplayWorldText>().Select(entry => Resolve(entry, resources, height)).OfType<WorldTextReadout>().ToArray();
    }

    private static WorldTextReadout? Resolve(GameplayWorldText entry, GameResStore resources, Func<Vector2, float> height)
    {
        var id = entry.TextId;
        var offset = -120f;
        // Gold 0x619104/0x619119 handles these text selectors explicitly.
        if (id == 0x3EC) offset -= 20;
        if (id == uint.MaxValue) { id = 0; offset -= 80; }
        var label = id == 0 ? "0" : resources.Strings.GetValueOrDefault(id);
        if (label is null) return null;
        var point = new Vector2(unchecked((uint)entry.X), unchecked((uint)entry.Y)) / SacredNetworkPosition.NativeUnitsPerTile;
        return new(new(point, height(point)), label, entry.ColorArgb, offset + entry.VerticalTravel);
    }
}
