using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny;
using Sacred.Granny.Animation;
using Sacred.Inventory.Actors;

namespace Sacred.Engine.Assets;

public sealed record PlayerCharacterAnimations(
    GrnAnimationClip Idle,
    GrnAnimationClip Walk,
    GrnAnimationClip Run,
    GrnAnimationClip Defend,
    GrnAnimationClip Attack);

internal static class CharacterWeaponStyleResolver
{
    public static CharacterMotionWeaponStyle Resolve(SacredGameActor actor)
    {
        var left = FindWeapon(actor, EquipmentSlotType.LeftHand);
        var right = FindWeapon(actor, EquipmentSlotType.RightHand);
        if (left is not null && right is not null &&
            left.Value.EquipmentType != SacredEquipmentType.Shield &&
            right.Value.EquipmentType != SacredEquipmentType.Shield)
        {
            return CharacterMotionWeaponStyle.DualWield;
        }

        var weapon = right is not null && right.Value.EquipmentType != SacredEquipmentType.Shield
            ? right
            : left is not null && left.Value.EquipmentType != SacredEquipmentType.Shield
                ? left
                : null;
        if (weapon is null)
            return CharacterMotionWeaponStyle.BareHanded;

        var equipment = weapon.Value;
        return equipment.EquipmentType switch
        {
            SacredEquipmentType.Dagger => CharacterMotionWeaponStyle.Dagger,
            SacredEquipmentType.Bow => CharacterMotionWeaponStyle.Bow,
            SacredEquipmentType.Crossbow => CharacterMotionWeaponStyle.Crossbow,
            SacredEquipmentType.Blade => equipment.InferredTwoHanded == true
                ? CharacterMotionWeaponStyle.TwoHandedBlade
                : CharacterMotionWeaponStyle.OneHandedBlade,
            SacredEquipmentType.TwoHandedSword => CharacterMotionWeaponStyle.TwoHanded,
            SacredEquipmentType.TwoHandedAxe => CharacterMotionWeaponStyle.TwoHandedAxe,
            SacredEquipmentType.LongHandled21 or
                SacredEquipmentType.BattleStaff or
                SacredEquipmentType.MageStaff or
                SacredEquipmentType.Briddle => CharacterMotionWeaponStyle.Staff,
            SacredEquipmentType.Pistol or
                SacredEquipmentType.Musket => CharacterMotionWeaponStyle.Pistol,
            SacredEquipmentType.Axe or
                SacredEquipmentType.OneHandedAxeOrMace
                when equipment.InferredTwoHanded == true => CharacterMotionWeaponStyle.TwoHandedAxe,
            SacredEquipmentType.Sword
                when equipment.InferredTwoHanded == true => CharacterMotionWeaponStyle.TwoHanded,
            _ => CharacterMotionWeaponStyle.OneHanded
        };
    }

    private static SacredEquipment? FindWeapon(
        SacredGameActor actor,
        EquipmentSlotType slotType)
    {
        foreach (var slot in actor.EquipmentSlots)
        {
            if (slot.Type == slotType && slot.Equipment is { } equipment && IsHandEquipment(equipment))
                return equipment;
        }

        return null;
    }

    private static bool IsHandEquipment(SacredEquipment equipment) =>
        equipment.EquipmentType is
            SacredEquipmentType.Sword or
            SacredEquipmentType.Dagger or
            SacredEquipmentType.TwoHandedSword or
            SacredEquipmentType.Axe or
            SacredEquipmentType.TwoHandedAxe or
            SacredEquipmentType.Shield or
            SacredEquipmentType.Bow or
            SacredEquipmentType.Crossbow or
            SacredEquipmentType.Blade or
            SacredEquipmentType.LongHandled21 or
            SacredEquipmentType.OneHandedAxeOrMace or
            SacredEquipmentType.BattleStaff or
            SacredEquipmentType.MageStaff or
            SacredEquipmentType.Briddle or
            SacredEquipmentType.Pistol or
            SacredEquipmentType.Musket;
}
