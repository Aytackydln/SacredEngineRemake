using System.Globalization;
using Sacred.Core.CombatArts;
using Sacred.Core.GameRes;
using Sacred.Inventory.Stats;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterStatLabels(GameResStore resources)
{
    public string Attribute(SacredActorStat stat) => stat switch
    {
        SacredActorStat.Strength => Text(1090, "Strength"), SacredActorStat.Endurance => Text(1091, "Endurance"),
        SacredActorStat.Dexterity => Text(1092, "Dexterity"), SacredActorStat.PhysicalRegeneration => Text(1093, "Physical Regeneration"),
        SacredActorStat.MentalRegeneration => Text(1094, "Mental Regeneration"), SacredActorStat.Charisma => Text(1095, "Charisma"),
        _ => stat.ToString()
    };
    public string Skill(byte id) => Text(9399 + id, $"Skill {id}");
    public string Art(ushort code) => SacredCombatArtDefinition.GetNameResource(code) is { } resource
        ? Text((int)resource, $"Combat art {code}") : $"Combat art {code}";
    public string Text(int id, string fallback) => resources.GetString(id.ToString(CultureInfo.InvariantCulture), fallback);
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
