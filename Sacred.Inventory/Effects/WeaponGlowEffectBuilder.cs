using System.Numerics;

namespace Sacred.Inventory.Effects;

/// <summary>Builds the fixed animated orb attached to a model's weapon_gl01 anchor.</summary>
internal static class WeaponGlowEffectBuilder
{
    private const float FlareSizeScale = 11.0f;
    private const string FlareTexture = "PARTICLE_FLARE03.TGA";
    private const string TrailTexture = "PARTICLE_LINE01.TGA";

    private static readonly Vector4 FlareColor = new(1.0f, 1.0f, 1.0f, 0.82f);
    private static readonly Vector4 TrailColor = new(0.58f, 0.48f, 1.0f, 0.52f);

    public static void Add(EffectMeshBuilder builder, Vector3 position, float unit)
    {
        builder.AddBillboard(
            position,
            unit * FlareSizeScale,
            unit * FlareSizeScale,
            FlareTexture,
            FlareColor,
            EquipmentEffectTextureMode.WeaponGlowFlare);
    }

    public static void AddTrail(EffectMeshBuilder builder, Vector3 start, Vector3 end, float unit) =>
        builder.AddCrossedStrip(
            start,
            end,
            unit * 0.42f,
            TrailTexture,
            TrailColor,
            EquipmentEffectTextureMode.Alpha);
}
