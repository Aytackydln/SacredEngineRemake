namespace Sacred.Core.GameBin.Balance;

/// <summary>Original authored-bonus value limits. Code-based selection follows Gold 0x5E22B0.</summary>
public sealed class SacredEquipmentBonusBalance
{
    public const int FirstIndividualCode = 800;
    public const int IndividualCount = 70;
    private readonly SacredEquipmentBonusCurveLayout[] _individual;

    public SacredEquipmentBonusBalance(IEnumerable<SacredEquipmentBonusCurveLayout> individual,
        SacredEquipmentBonusCurveLayout specialMove, SacredEquipmentBonusCurveLayout spell,
        SacredEquipmentBonusCurveLayout skill, SacredEquipmentBonusCurveLayout other)
    {
        _individual = individual.ToArray();
        if (_individual.Length != IndividualCount) throw new ArgumentException("Expected seventy individual bonus curves.", nameof(individual));
        (SpecialMove, Spell, Skill, Other) = (specialMove, spell, skill, other);
    }
    public SacredEquipmentBonusCurveLayout SpecialMove { get; }
    public SacredEquipmentBonusCurveLayout Spell { get; }
    public SacredEquipmentBonusCurveLayout Skill { get; }
    public SacredEquipmentBonusCurveLayout Other { get; }
    public SacredEquipmentBonusCurveLayout Get(ushort code) => code switch
    {
        < 200 => SpecialMove,
        > 999 => Spell,
        >= 600 and < 800 => Skill,
        >= FirstIndividualCode and < FirstIndividualCode + IndividualCount => _individual[code - FirstIndividualCode],
        _ => Other
    };
}
