using System.Globalization;
using Sacred.Core.GameRes;

namespace Sacred.Core.Pak.Weapon.Descriptions;

internal sealed class SacredEquipmentDescriptionText(GameResStore resources)
{
    public string Get(int key, string? fallback = null)
    {
        var resourceKey = Number(key);
        return resources.GetString(resourceKey, fallback ?? resourceKey);
    }

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Signed(int value) => value.ToString("+0;-0;+0", CultureInfo.InvariantCulture);

    public static string Range(int minimum, int maximum) =>
        minimum == maximum ? Number(minimum) : $"{Number(minimum)}–{Number(maximum)}";
}
