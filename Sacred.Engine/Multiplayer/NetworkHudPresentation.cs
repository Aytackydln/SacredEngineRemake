using System;
using System.Collections.Generic;
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
    private readonly List<AllyHudEntry> _allies = [];
    public void Update(GoldWorldReplica world, GameResStore resources, SceneState scene, float seconds)
    {
        _received ??= new(world); _dragDrop ??= new(world);
        _chat ??= new(world); _chat.Advance(seconds, resources);
        _received.Advance(seconds, resources); _dragDrop.Update();
        scene.CombatText.HudNotices = _received.Notices;
        scene.CombatText.ChatLines = _chat.Lines;
        scene.CombatText.DragDropClears = _dragDrop.Cleared;
        _allies.Clear();
        var teams = world.Session.Teams;
        var localSlot = world.LocalPlayerSlot;
        var team = teams.GetTeam(localSlot);
        if (team != 0)
        {
            var roster = world.Session.Lobby.Control.Roster;
            for (byte slot = 1; slot <= GoldTeamCodec.PlayerCount; slot++)
            {
                if (slot == localSlot || teams.GetTeam(slot) != team || roster.Find(slot) is not { IsPresent: true } player)
                    continue;
                float? health = null;
                uint? actorType = null;
                if (world.Objects.TryGetValue(slot, out var actor) && actor.BaseState.Owner == slot &&
                    actor.RemovalOpcode is null && actor.Creature is not null)
                {
                    actorType = actor.BaseState.ObjectType;
                    if (actor.Values is { MaximumHitPoints: > 0 } values)
                        health = actor.IsDead ? 0 : Math.Clamp((actor.CurrentHitPoints ?? values.CurrentHitPoints) / (float)values.MaximumHitPoints, 0, 1);
                }
                var name = player.Name;
                _allies.Add(new(string.IsNullOrWhiteSpace(name) ? $"Player {slot}" : name, actorType, teams.IsLeader(slot), health));
            }
        }
        scene.Allies = _allies;
    }
}
