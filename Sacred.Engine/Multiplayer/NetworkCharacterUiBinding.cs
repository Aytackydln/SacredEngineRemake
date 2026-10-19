using System;
using System.Linq;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Stats;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>One local received loadout shared by the existing equipment, quick-slot and portrait views.</summary>
internal sealed class NetworkCharacterUiBinding(AssetManager assets, SceneState scene)
{
    private readonly GoldItemState?[] _items = new GoldItemState?[GoldCreatureStateLayout.EquipmentCount];
    private string? _appearance;
    private SacredGameActor? _actor;
    private GoldCreatureValuesState? _values;
    private SacredMovementSpeeds? _movement;
    private readonly NetworkQuickbarUiBinding _quickbar = new(assets, scene.Hud!);

    public void Update(GoldWorldReplica world)
    {
        if (!world.Objects.TryGetValue(world.LocalPlayerSlot, out var hero) || hero.Creature is null) return;
        var key = NetworkActorAppearance.Key(hero, world);
        var changed = key != _appearance;
        var references = hero.Creature.EquipmentReferences;
        for (var index = 0; index < references.Length; index++)
        {
            var item = world.Objects.TryGetValue(references[index], out var equipment) ? equipment.Item : null;
            changed |= !ReferenceEquals(item, _items[index]);
            _items[index] = item;
        }
        if (changed)
        {
            _actor = NetworkActorAppearance.Create(assets, hero, world);
            _appearance = key;
            _values = null;
            scene.EquipmentUi.Bind(_actor);
            scene.Portraits.BindPlayer(_actor);
            scene.Hud!.Visible = true;
            EngineLog.WriteLine($"Network character UI bound: ref {world.LocalPlayerSlot}; {_actor.EquipmentSlots.Count(slot => slot.Instance is not null)} received equipment instances; portrait and quick slots share the local actor.");
        }
        if (_actor is not null && hero.Values is { } values && !ReferenceEquals(_values, values))
        {
            if (values.Level != 0)
            {
                if (values.IsFull) _actor.Progression.ImportNativeObjectValues(values.FullBlock08);
                _actor.Progression.ImportNativeProgression(values.Level, values.Experience, values.Gold, values.RemainingSkillPoints,
                    values.Block1C, values.Block24, values.Block2C, values.Byte58);
            }
            if (values.MaximumHitPoints > 0)
                _actor.Character.Health.SetHitPoints(Math.Clamp(values.CurrentHitPoints, 0, values.MaximumHitPoints), values.MaximumHitPoints);
            _values = values;
        }
        if (_actor is not null)
        {
            _actor.SetMovementRuntime(hero.Creature.AnimationSpeedMultiplier, hero.BaseState.Flags, hero.Values?.Word148 ?? 0, hero.Creature.BehaviorFlags);
            if (_actor.Movement is { } movement && _movement != movement)
            {
                _movement = movement;
                EngineLog.WriteLine($"Network movement speed calculated: {movement.Percent}%; walk {movement.Walk} native units/s ({movement.WalkTilesPerSecond:0.###} tiles/s), run {movement.Run} ({movement.RunTilesPerSecond:0.###} tiles/s); animation factor {_actor.LocomotionPlaybackSpeed:0.###} before motion multiplier; profile {_actor.Progression.MovementProfileSelector}.");
            }
        }
        if (_actor is not null) _quickbar.Update(world, _actor);
    }
}
