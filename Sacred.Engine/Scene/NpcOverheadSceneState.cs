using System;
using System.Collections.Generic;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Scene;

/// <summary>Presentation of existing actor instances. It never creates campaign NPCs.</summary>
public sealed class NpcOverheadSceneState
{
    public SacredCamera? Camera { get; set; }
    public bool ShowStates { get; set; }
    public bool? ShowStatesOverride { get; set; }
    public IReadOnlyList<NpcOverheadActor> Actors { get; set; } = Array.Empty<NpcOverheadActor>();
    public Func<uint, uint?> DialogIcon { get; set; } = _ => null;
    public int DrawnIcons { get; internal set; }
    public int DrawnGoldenIcons { get; internal set; }
    public int DrawnGlowLayers { get; internal set; }
    public uint AnimationMilliseconds { get; internal set; }
    public uint? AnimationMillisecondsOverride { get; set; }
    public string ArtworkStatus { get; internal set; } = "loading";
}

public sealed record NpcOverheadActor(uint Reference, SceneModel Model, SacredNpcOverheadState State,
    bool TemplateDialog, bool Animal);
