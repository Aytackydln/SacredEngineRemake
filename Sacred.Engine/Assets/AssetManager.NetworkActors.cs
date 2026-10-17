using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Assets;

public sealed partial class AssetManager
{
    internal async Task<PlayerCharacterAsset> LoadNetworkActorAsync(ItemsPakEntry item, SacredGameActor actor,
        CancellationToken cancellationToken)
    {
        var attachments = ResolvePlayerCharacterItems(actor);
        var model = await _modelsPak.LoadCharacterBaseModelAsync(item.ModelName,
            CreateModelAttachmentReferences(attachments), cancellationToken).ConfigureAwait(false);
        var attachedModels = await Task.WhenAll(attachments.Select(attachment =>
            LoadPlayerAttachmentModelAsync(attachment.Item.ModelName, cancellationToken))).ConfigureAwait(false);
        return new(item.ItemIndex, item.ModelName, item.ModelName, model,
            CreatePlayerCharacterTextureAliases(model, item, attachments, attachedModels),
            EquipmentEffectSceneFactory.Create(model, CreateEquipmentEffectAttachments(attachments, attachedModels), true),
            CharacterWeaponStyleResolver.Resolve(actor));
    }

    internal async Task<PlayerCharacterAnimations?> LoadNetworkAnimationsAsync(PlayerCharacterAsset asset,
        SacredCreatureTemplate? template, CancellationToken cancellationToken)
    {
        var talk = await _modelsPak.LoadModelAnimationAsync(asset.ModelName,
            SacredNpcConversationEffect.TalkMotionSlot, cancellationToken).ConfigureAwait(false);
        // The rare reaction calls native selector 4, which considers mount/model availability
        // as well as weapon style. Keep it pending until that selector is ported.
        if (template?.Class == SacredCreatureClass.Hero)
        {
            var hero = await LoadPlayerCharacterAnimationsAsync(asset, cancellationToken).ConfigureAwait(false);
            return hero is null ? null : hero with { NpcTalk = talk };
        }
        // Non-hero model motions use the native locomotion selectors 0/1/2.
        var idle = await _modelsPak.LoadCharacterAnimationAsync(asset.ModelName, CharacterMotionKind.Idle, asset.WeaponStyle, cancellationToken).ConfigureAwait(false)
            ?? await _modelsPak.LoadModelAnimationAsync(asset.ModelName, 0, cancellationToken).ConfigureAwait(false)
            ?? await _modelsPak.LoadDefaultCharacterAnimationAsync(asset.ModelName, cancellationToken).ConfigureAwait(false);
        if (idle is null) return null;
        var walk = await _modelsPak.LoadCharacterAnimationAsync(asset.ModelName, CharacterMotionKind.Walk, asset.WeaponStyle, cancellationToken).ConfigureAwait(false)
            ?? await _modelsPak.LoadModelAnimationAsync(asset.ModelName, 1, cancellationToken).ConfigureAwait(false) ?? idle;
        var run = await _modelsPak.LoadCharacterAnimationAsync(asset.ModelName, CharacterMotionKind.Run, asset.WeaponStyle, cancellationToken).ConfigureAwait(false)
            ?? await _modelsPak.LoadModelAnimationAsync(asset.ModelName, 2, cancellationToken).ConfigureAwait(false) ?? walk;
        return new(idle, walk, run, idle, idle) { NpcTalk = talk };
    }

    internal SacredGameActor CreateNetworkActor(SacredCreatureTemplate? template)
    {
        var mask = template?.PlayableClassMask ?? 0;
        var characterClass = Enum.GetValues<SacredCharacterClass>().FirstOrDefault(value => value.ToMask() == mask);
        var actor = template?.Class == SacredCreatureClass.Hero ? new SacredGameActor(characterClass, template)
            : new SacredGameActor(template, Enum.GetValues<EquipmentSlotType>());
        if (MovementCalculator is { } movement) actor.ConfigureMovement(movement);
        if (template?.Class == SacredCreatureClass.Hero)
            SacredActorAppearance.Configure(actor, _equipmentByModelId.Values);
        return actor;
    }
}
