namespace Sacred.Core.Pak.Weapon.Details;

public sealed record SacredEquipmentIdentity(
    uint NameResourceKey,
    string AuthoredName,
    SacredEquipmentType Type,
    SacredCharacterClassMask Classes,
    SacredEquipmentHandedness Handedness,
    SacredEquipmentSetReference? Set,
    uint Price,
    byte Level,
    int SocketCount,
    int OccupiedSocketCount);
