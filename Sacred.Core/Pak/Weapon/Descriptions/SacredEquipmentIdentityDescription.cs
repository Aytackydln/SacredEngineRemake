using System.Text;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Details;
using static Sacred.Core.Pak.Weapon.Descriptions.SacredEquipmentDescriptionText;

namespace Sacred.Core.Pak.Weapon.Descriptions;

internal sealed class SacredEquipmentIdentityDescription(GameResStore resources)
{
    private readonly SacredEquipmentDescriptionText _text = new(resources);

    public SacredEquipmentDescriptionSection Create(SacredEquipmentIdentity identity)
    {
        var fields = new List<SacredEquipmentDescriptionField>
        {
            new("Type", EquipmentTypeName(identity.Type)),
            new("Classes", ClassNames(identity.Classes))
        };

        var handedness = identity.Handedness switch
        {
            SacredEquipmentHandedness.OneHanded => "One-handed",
            SacredEquipmentHandedness.TwoHanded => "Two-handed",
            _ => null
        };
        if (handedness is not null)
            fields.Add(new("Handedness", handedness));

        if (identity.Set is { } set)
            fields.Add(new("Set", SetName(set)));

        fields.Add(new(_text.Get(1055, "Price"), Number(identity.Price)));
        fields.Add(new(_text.Get(1070, "Level"), Number(identity.Level)));
        fields.Add(new("Sockets", $"{identity.OccupiedSocketCount}/{identity.SocketCount}"));

        return new("Item", fields);
    }

    private string SetName(SacredEquipmentSetReference set)
    {
        var fallback = resources.GetString($"SetName{set.Index}", $"Set {set.Index}");
        return set.NameResourceId is { } resourceId ? resources.Strings.GetValueOrDefault(resourceId, fallback) : fallback;
    }

    private string ClassNames(SacredCharacterClassMask mask)
    {
        if (mask == SacredCharacterClassMask.None)
            return "Unspecified";
        if (mask == SacredCharacterClassMask.AllKnown)
            return "All classes";

        var names = new List<string>();
        for (var bit = 0; bit < 8; bit++)
        {
            var characterClass = (SacredCharacterClassMask)(1 << bit);
            if ((mask & characterClass) != 0)
                names.Add(_text.Get(bit < 6 ? bit + 1 : bit + 2, characterClass.ToString()));
        }

        return string.Join(", ", names);
    }

    private static string EquipmentTypeName(SacredEquipmentType type)
    {
        var name = type.ToString();
        var result = new StringBuilder(name.Length);
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
                result.Append(' ');
            result.Append(name[index]);
        }

        return result.ToString();
    }
}
