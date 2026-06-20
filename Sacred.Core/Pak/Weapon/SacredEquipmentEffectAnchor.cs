namespace Sacred.Core.Pak.Weapon;

/// <summary>
/// Describes the reserved GRN bone-name conventions used by Sacred's equipped-item effects.
/// The bone position is supplied by the model; Weapon.pak damage ranges activate elemental emitters.
/// </summary>
public readonly record struct SacredEquipmentEffectAnchor(
    string BoneName,
    SacredEquipmentEffectAnchorKind Kind,
    int Index)
{
    public static bool TryParse(string? boneName, out SacredEquipmentEffectAnchor anchor)
    {
        if (TryParseIndexedName(boneName, "weapon_fx", SacredEquipmentEffectAnchorKind.ElementalEmitter, out anchor) ||
            TryParseIndexedName(boneName, "weapon_gl", SacredEquipmentEffectAnchorKind.Glow, out anchor) ||
            TryParseIndexedName(boneName, "stdfx_bone", SacredEquipmentEffectAnchorKind.StandardEffect, out anchor) ||
            TryParseIndexedName(boneName, "fx_streak", SacredEquipmentEffectAnchorKind.Streak, out anchor))
        {
            return true;
        }

        anchor = default;
        return false;
    }

    private static bool TryParseIndexedName(
        string? boneName,
        string prefix,
        SacredEquipmentEffectAnchorKind kind,
        out SacredEquipmentEffectAnchor anchor)
    {
        anchor = default;
        if (string.IsNullOrWhiteSpace(boneName) ||
            !boneName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(boneName.AsSpan(prefix.Length), out var index) ||
            index <= 0)
        {
            return false;
        }

        anchor = new SacredEquipmentEffectAnchor(boneName, kind, index);
        return true;
    }
}

public enum SacredEquipmentEffectAnchorKind
{
    ElementalEmitter,
    Glow,
    StandardEffect,
    Streak
}
