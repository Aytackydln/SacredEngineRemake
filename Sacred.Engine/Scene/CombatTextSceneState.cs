using System.Collections.Generic;

namespace Sacred.Engine.Scene;

public sealed class CombatTextSceneState
{
    public IReadOnlyList<CombatDamageText> Damage { get; internal set; } = [];
    public int DrawnCount { get; internal set; }
}

public sealed record CombatDamageText(SceneModel Model, ushort Amount, float AgeSeconds);
