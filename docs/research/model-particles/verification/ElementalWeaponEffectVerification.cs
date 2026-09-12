using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;
using Sacred.Particles.Particles;
using Sacred.Inventory.Effects;
using Sacred.Particles;

internal static class ElementalWeaponEffectVerification
{
    public static void Run()
    {
        Require(Marshal.SizeOf<SacredElementalWeaponParticleStateLayout>() == 0xA4,
            "MAGICFIRE/MAGICGIFT state layout size");
        Require(Select(0, 80, 80, 0) is null, "equal elemental maxima must not select an effect");
        Require(Select(300, 100, 0, 0) is null, "elemental damage equal to one third physical must not select");

        var fire = Select(0, 60, 0, 0);
        Require(fire?.Definition.Kind == SacredElementalWeaponEffectKind.Fire, "fire dominance");
        RequireClose(fire!.Value.Intensity, (60f - 12.5f) / (275f / 3f - 12.5f), "fire intensity");

        var magic = Select(0, 0, 40, 0);
        Require(magic?.Definition.Kind == SacredElementalWeaponEffectKind.Magic, "magic dominance");
        RequireClose(magic!.Value.Intensity, (40f - 25f / 3f) / (275f / 4f - 25f / 3f), "magic intensity");

        var poison = Select(0, 0, 0, 500);
        Require(poison?.Definition.Kind == SacredElementalWeaponEffectKind.Poison && poison.Value.Intensity == 1,
            "poison intensity clamp");
    }

    private static SelectedElementalWeaponEffect? Select(ushort physical, ushort fire, ushort magic, ushort poison) =>
        ElementalWeaponEffectSelector.Select(new SacredEquipmentDamage(
            0, 0, 0, 0, physical, fire, magic, poison));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException($"Elemental weapon verification failed: {message}");
    }

    private static void RequireClose(float actual, float expected, string message) =>
        Require(MathF.Abs(actual - expected) < 0.00001f, $"{message}: {actual} != {expected}");
}
