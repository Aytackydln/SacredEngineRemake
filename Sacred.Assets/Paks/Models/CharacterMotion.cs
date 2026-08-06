namespace Sacred.Assets.Paks.Models;

public enum CharacterMotionKind
{
    Idle,
    Walk,
    Run,
    Defend,
    Attack
}

/// <summary>
/// The weapon-pose columns stored in each character record in Models.tmp.
/// </summary>
public enum CharacterMotionWeaponStyle
{
    BareHanded,
    OneHanded,
    TwoHanded,
    TwoHandedAxe,
    Staff,
    Dagger,
    Throwing,
    Bow,
    DualWield,
    OneHandedBlade,
    TwoHandedBlade,
    Whip,
    Crossbow,
    Pistol
}
