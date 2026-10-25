using System.Collections.Generic;
using Sacred.GameplayHost.World;

namespace Sacred.Engine.Scene;

public sealed class CombatTextSceneState
{
    public IReadOnlyList<CombatDamageText> Damage { get; internal set; } = [];
    public int DrawnCount { get; internal set; }
    public IReadOnlyList<WorldTextReadout> WorldText { get; internal set; } = [];
    public int DrawnWorldText { get; internal set; }
    public long DroppedWorldText { get; internal set; }
    public IReadOnlyList<GameplayHudNotice> HudNotices { get; internal set; } = [];
    public int DrawnHudNotices { get; internal set; }
    public IReadOnlyList<GameplayHudNotice> ChatLines { get; internal set; } = [];
    public int DrawnChatLines { get; internal set; }
    public long DragDropClears { get; internal set; }
}

public sealed record CombatDamageText(SceneModel Model, ushort Amount, float AgeSeconds);
