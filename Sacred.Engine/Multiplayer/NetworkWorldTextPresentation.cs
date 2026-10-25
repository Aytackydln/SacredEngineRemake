using System;
using System.Collections.Generic;
using System.Globalization;
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
    private GameplayReceivedTexts? _received;

    public void Update(GoldWorldReplica world, GameResStore resources, SceneState scene, Func<Vector2, float> height, float seconds)
    {
        _received ??= new(world);
        _received.Advance(seconds);
        scene.CombatText.WorldText = _received.Pool.Entries.Select(entry => Resolve(entry, resources, height))
            .OfType<WorldTextReadout>().ToArray();
        scene.CombatText.DroppedWorldText = _received.Pool.Dropped;
    }

    private static WorldTextReadout? Resolve(GameplayWorldText entry, GameResStore resources, Func<Vector2, float> height)
    {
        var label = entry.Resource == 0 ? entry.Digit.ToString(CultureInfo.InvariantCulture)
            : resources.Strings.GetValueOrDefault(entry.Resource);
        if (label is null) return null;
        var point = new Vector2(unchecked((uint)entry.X), unchecked((uint)entry.Y)) / SacredNetworkPosition.NativeUnitsPerTile;
        return new(new(point, height(point)), label, entry.ColorArgb, entry.VerticalOffset);
    }
}
