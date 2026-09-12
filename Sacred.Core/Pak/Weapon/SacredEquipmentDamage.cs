using System.Runtime.InteropServices;
using System.Text;

namespace Sacred.Core.Pak.Weapon;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct SacredEquipmentDamage(
    ushort PhysicalDamageMinimum,
    ushort FireDamageMinimum,
    ushort MagicDamageMinimum,
    ushort PoisonDamageMinimum,
    ushort PhysicalDamageMaximum,
    ushort FireDamageMaximum,
    ushort MagicDamageMaximum,
    ushort PoisonDamageMaximum
    )
{
    public bool HasPhysicalDamage => PhysicalDamageMinimum != 0 || PhysicalDamageMaximum != 0;
    public bool HasFireDamage => FireDamageMinimum != 0 || FireDamageMaximum != 0;
    public bool HasMagicDamage => MagicDamageMinimum != 0 || MagicDamageMaximum != 0;
    public bool HasPoisonDamage => PoisonDamageMinimum != 0 || PoisonDamageMaximum != 0;
    
    public override string ToString()
    {
        var phys = HasPhysicalDamage ? $"{PhysicalDamageMinimum}-{PhysicalDamageMaximum} phys" : "";
        var fire = HasFireDamage ? $"{FireDamageMinimum}-{FireDamageMaximum} fire" : "";
        var magic = HasMagicDamage ? $"{MagicDamageMinimum}-{MagicDamageMaximum} magic" : "";
        var pois = HasPoisonDamage ? $"{PoisonDamageMinimum}-{PoisonDamageMaximum} poison" : "";
        var strings = new[]
        {
            phys, fire, magic, pois
        };
        return string.Join(' ', strings);
    }

    private bool PrintMembers(StringBuilder builder)
    {
        var phys = HasPhysicalDamage ? $"{PhysicalDamageMinimum}-{PhysicalDamageMaximum} phys" : "";
        var fire = HasFireDamage ? $"{FireDamageMinimum}-{FireDamageMaximum} fire" : "";
        var magic = HasMagicDamage ? $"{MagicDamageMinimum}-{MagicDamageMaximum} magic" : "";
        var pois = HasPoisonDamage ? $"{PoisonDamageMinimum}-{PoisonDamageMaximum} poison" : "";

        builder.Append(phys);
        builder.Append(fire);
        builder.Append(magic);
        builder.Append(pois);

        return true;
    }
}
