using System.Numerics;
using Sacred.Engine.Animation;
using Sacred.Engine.Assets;
using Sacred.Inventory.Actors;

namespace Sacred.Engine.Scene.InGame;

internal readonly record struct PlayerCombatContext(SacredGameActor Actor, PlayerCharacterAsset Asset,
    CharacterAnimationState Animation, SceneModel Model);

internal readonly record struct PlayerCombatRequest(bool Cast, Vector2 Target, bool SelfTarget = false);
