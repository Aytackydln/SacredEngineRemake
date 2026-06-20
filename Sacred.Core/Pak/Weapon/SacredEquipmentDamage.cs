using System.Text;

namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredDamageRange(ushort Minimum, ushort Maximum)
{
    public bool IsPresent => Minimum != 0 || Maximum != 0;
}

public readonly record struct SacredEquipmentDamage(
    SacredDamageRange Physical,
    SacredDamageRange Fire,
    SacredDamageRange Magic,
    SacredDamageRange Poison)
{
    public bool HasElementalDamage => Fire.IsPresent || Magic.IsPresent || Poison.IsPresent;

    public override string ToString()
    {
        var phys = Physical.IsPresent ? $"{Physical.Minimum}-{Physical.Maximum} phys" : "";
        var fire = Fire.IsPresent ? $"{Fire.Minimum}-{Fire.Maximum} fire" : "";
        var magic = Magic.IsPresent ? $"{Magic.Minimum}-{Magic.Maximum} magic" : "";
        var pois = Poison.IsPresent ? $"{Poison.Minimum}-{Poison.Maximum} poison" : "";
        var strings = new[]
        {
            phys, fire, magic, pois
        };
        return string.Join(' ', strings);
    }

    private bool PrintMembers(StringBuilder builder)
    {
        var phys = Physical.IsPresent ? $"{Physical.Minimum}-{Physical.Maximum} phys" : "";
        var fire = Fire.IsPresent ? $"{Fire.Minimum}-{Fire.Maximum} fire" : "";
        var magic = Magic.IsPresent ? $"{Magic.Minimum}-{Magic.Maximum} magic" : "";
        var pois = Poison.IsPresent ? $"{Poison.Minimum}-{Poison.Maximum} poison" : "";

        builder.Append(phys);
        builder.Append(fire);
        builder.Append(magic);
        builder.Append(pois);

        return true;
    }
}
