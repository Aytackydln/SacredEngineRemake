using Sacred.Inventory.Actors;

namespace Sacred.UI.Character;

/// <summary>Live actor references for the player and companion portraits.</summary>
public sealed class ActorPortraitPanelState
{
    public SacredGameActor? Player { get; private set; }
    public SacredActorParty Party { get; } = new();
    public ActorPortraitLayout? Layout { get; set; }
    public void BindPlayer(SacredGameActor actor) => Player = actor;
    public void ClearPlayer() => Player = null;
}
