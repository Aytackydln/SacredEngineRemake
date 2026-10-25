using Sacred.Core.GameRes;
using Sacred.Engine.Scene;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal sealed class NetworkHudPresentation
{
    private GameplayReceivedHud? _received;
    private GameplayReceivedDragDrop? _dragDrop;
    private GameplayReceivedChat? _chat;
    public void Update(GoldWorldReplica world, GameResStore resources, SceneState scene, float seconds)
    {
        _received ??= new(world); _dragDrop ??= new(world);
        _chat ??= new(world); _chat.Advance(seconds, resources);
        _received.Advance(seconds, resources); _dragDrop.Update();
        scene.CombatText.HudNotices = _received.Notices;
        scene.CombatText.ChatLines = _chat.Lines;
        scene.CombatText.DragDropClears = _dragDrop.Cleared;
    }
}
